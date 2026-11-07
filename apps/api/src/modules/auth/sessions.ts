import { accounts, newId, refreshTokens, type AccountStatus } from '@kindling/db';
import { and, eq, isNull } from 'drizzle-orm';
import type { AppContext } from '../../context.js';
import { randomToken, sha256 } from '../../lib/crypto.js';
import { ProblemError } from '../../lib/problem.js';
import { ErrorCodes } from '@kindling/contracts';

export const ACCESS_TOKEN_AUDIENCE = 'kindling:player';
const REVOKED_SID_PREFIX = 'auth:revoked-sid:';

export interface SessionMeta {
  ip: string;
  userAgent?: string | undefined;
  clientVersion?: string | undefined;
}

export interface IssuedSession {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

export interface AccessClaims {
  accountId: string;
  sessionId: string;
}

/** Statuses that may hold a session. pending_deletion can sign in so the player can cancel. */
export function canHoldSession(status: AccountStatus): boolean {
  return status === 'active' || status === 'pending_deletion';
}

export class SessionService {
  constructor(private readonly ctx: AppContext) {}

  private refreshExpiry(now: Date): Date {
    return new Date(now.getTime() + this.ctx.config.REFRESH_TOKEN_TTL_DAYS * 86_400_000);
  }

  private async signAccess(accountId: string, familyId: string, now: Date) {
    const ttl = this.ctx.config.ACCESS_TOKEN_TTL_SECONDS;
    const issuedAt = Math.floor(now.getTime() / 1000);
    const accessToken = await this.ctx.keys.access.sign(
      { scope: 'player', sid: familyId },
      { subject: accountId, audience: ACCESS_TOKEN_AUDIENCE, expiresInSeconds: ttl, issuedAt },
    );
    return { accessToken, accessTokenExpiresAt: new Date((issuedAt + ttl) * 1000).toISOString() };
  }

  /** Starts a new refresh-token family (one per sign-in on one device). */
  async start(accountId: string, meta: SessionMeta): Promise<IssuedSession> {
    const now = this.ctx.now();
    const familyId = newId();
    const refreshToken = randomToken();
    const expiresAt = this.refreshExpiry(now);
    await this.ctx.db.insert(refreshTokens).values({
      id: newId(),
      accountId,
      familyId,
      tokenHash: sha256(refreshToken),
      expiresAt,
      clientVersion: meta.clientVersion ?? null,
      userAgent: meta.userAgent?.slice(0, 256) ?? null,
      createdIpHash: this.ctx.hashIp(meta.ip),
    });
    return {
      ...(await this.signAccess(accountId, familyId, now)),
      refreshToken,
      refreshTokenExpiresAt: expiresAt.toISOString(),
    };
  }

  /**
   * Rotates a refresh token. Presenting a token that was already rotated means it was copied
   * (or the client retried after a lost response): the whole family is revoked either way.
   * Clients must serialize refresh calls.
   */
  async refresh(refreshToken: string, meta: SessionMeta): Promise<IssuedSession & { accountId: string }> {
    const now = this.ctx.now();
    const [row] = await this.ctx.db
      .select({
        id: refreshTokens.id,
        accountId: refreshTokens.accountId,
        familyId: refreshTokens.familyId,
        expiresAt: refreshTokens.expiresAt,
        rotatedAt: refreshTokens.rotatedAt,
        revokedAt: refreshTokens.revokedAt,
        status: accounts.status,
      })
      .from(refreshTokens)
      .innerJoin(accounts, eq(accounts.id, refreshTokens.accountId))
      .where(eq(refreshTokens.tokenHash, sha256(refreshToken)))
      .limit(1);

    if (!row || row.revokedAt) throw ProblemError.unauthorized(ErrorCodes.invalidRefreshToken);
    if (row.rotatedAt) {
      await this.revokeFamily(row.familyId, 'reuse_detected');
      throw ProblemError.unauthorized(
        ErrorCodes.refreshTokenReused,
        'This session was ended for security reasons.',
      );
    }
    if (row.expiresAt <= now)
      throw ProblemError.unauthorized(ErrorCodes.invalidRefreshToken, 'Session expired.');
    if (!canHoldSession(row.status)) {
      await this.revokeFamily(row.familyId, 'account_inactive');
      throw ProblemError.forbidden(ErrorCodes.accountSuspended);
    }

    const nextToken = randomToken();
    const expiresAt = this.refreshExpiry(now);
    const rotated = await this.ctx.db.transaction(async (tx) => {
      const updated = await tx
        .update(refreshTokens)
        .set({ rotatedAt: now })
        .where(
          and(eq(refreshTokens.id, row.id), isNull(refreshTokens.rotatedAt), isNull(refreshTokens.revokedAt)),
        )
        .returning({ id: refreshTokens.id });
      if (updated.length === 0) return false; // lost a race with a concurrent refresh
      await tx.insert(refreshTokens).values({
        id: newId(),
        accountId: row.accountId,
        familyId: row.familyId,
        tokenHash: sha256(nextToken),
        expiresAt,
        clientVersion: meta.clientVersion ?? null,
        userAgent: meta.userAgent?.slice(0, 256) ?? null,
        createdIpHash: this.ctx.hashIp(meta.ip),
      });
      return true;
    });
    if (!rotated) {
      await this.revokeFamily(row.familyId, 'reuse_detected');
      throw ProblemError.unauthorized(
        ErrorCodes.refreshTokenReused,
        'This session was ended for security reasons.',
      );
    }

    return {
      accountId: row.accountId,
      ...(await this.signAccess(row.accountId, row.familyId, now)),
      refreshToken: nextToken,
      refreshTokenExpiresAt: expiresAt.toISOString(),
    };
  }

  /** Revokes one family (logout on one device) and blocks its outstanding access tokens. */
  async revokeFamily(familyId: string, reason: string): Promise<void> {
    await this.ctx.db
      .update(refreshTokens)
      .set({ revokedAt: this.ctx.now(), revokeReason: reason })
      .where(and(eq(refreshTokens.familyId, familyId), isNull(refreshTokens.revokedAt)));
    await this.blockAccess([familyId]);
  }

  /** Signs the account out everywhere (password reset, deletion, suspension). */
  async revokeAllForAccount(accountId: string, reason: string): Promise<void> {
    const rows = await this.ctx.db
      .update(refreshTokens)
      .set({ revokedAt: this.ctx.now(), revokeReason: reason })
      .where(and(eq(refreshTokens.accountId, accountId), isNull(refreshTokens.revokedAt)))
      .returning({ familyId: refreshTokens.familyId });
    await this.blockAccess([...new Set(rows.map((r) => r.familyId))]);
  }

  /** Revokes the family a refresh token belongs to. Unknown tokens are ignored (logout is idempotent). */
  async revokeByRefreshToken(refreshToken: string): Promise<void> {
    const [row] = await this.ctx.db
      .select({ familyId: refreshTokens.familyId })
      .from(refreshTokens)
      .where(eq(refreshTokens.tokenHash, sha256(refreshToken)))
      .limit(1);
    if (row) await this.revokeFamily(row.familyId, 'logout');
  }

  private async blockAccess(familyIds: string[]): Promise<void> {
    if (familyIds.length === 0) return;
    const ttl = this.ctx.config.ACCESS_TOKEN_TTL_SECONDS + 60;
    const pipeline = this.ctx.redis.pipeline();
    for (const id of familyIds) pipeline.set(`${REVOKED_SID_PREFIX}${id}`, '1', 'EX', ttl);
    await pipeline.exec();
  }

  /** Validates an access token. Revocation is checked in Redis; if Redis is down we fail open (tokens live ≤ 15 min). */
  async verifyAccessToken(token: string): Promise<AccessClaims> {
    let payload;
    try {
      payload = await this.ctx.keys.access.verify(token, {
        audience: ACCESS_TOKEN_AUDIENCE,
        currentDate: this.ctx.now(),
      });
    } catch {
      throw ProblemError.unauthorized(ErrorCodes.unauthorized, 'Invalid or expired access token.');
    }
    const sid = payload['sid'];
    if (typeof payload.sub !== 'string' || typeof sid !== 'string' || payload['scope'] !== 'player') {
      throw ProblemError.unauthorized(ErrorCodes.unauthorized, 'Invalid access token.');
    }
    try {
      if (await this.ctx.redis.exists(`${REVOKED_SID_PREFIX}${sid}`)) {
        throw ProblemError.unauthorized(ErrorCodes.unauthorized, 'Session has ended.');
      }
    } catch (err) {
      if (err instanceof ProblemError) throw err;
      // Redis unavailable: accept the (short-lived) token rather than taking the API down.
    }
    return { accountId: payload.sub, sessionId: sid };
  }
}
