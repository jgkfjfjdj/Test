import { ErrorCodes } from '@kindling/contracts';
import {
  accounts,
  authIdentities,
  emailTokens,
  newId,
  profiles,
  type AccountStatus,
  type EmailTokenPurpose,
} from '@kindling/db';
import { and, eq, gt, isNull } from 'drizzle-orm';
import type { AppContext } from '../../context.js';
import { AppleTokenError } from '../../lib/apple.js';
import { randomToken, sha256 } from '../../lib/crypto.js';
import {
  checkPasswordPolicy,
  hashPassword,
  verifyAgainstDummy,
  verifyPassword,
  type PasswordProblem,
} from '../../lib/passwords.js';
import { pgError, ProblemError } from '../../lib/problem.js';
import { checkDisplayName, DISPLAY_NAME_RULES, displayNameKey } from '../accounts/display-name.js';
import { normalizeEmail } from './email.js';
import { canHoldSession, SessionService, type IssuedSession, type SessionMeta } from './sessions.js';

export interface AccountSummary {
  id: string;
  displayName: string;
  email: string | null;
  emailVerified: boolean;
  status: AccountStatus;
}

export interface AuthResult extends IssuedSession {
  account: AccountSummary;
  /** True when this request created the account. */
  created: boolean;
}

const VERIFY_TTL_MS = 48 * 3600_000;
const RESET_TTL_MS = 3600_000;
const FAILED_LOGIN_LIMIT = 10;
const FAILED_LOGIN_WINDOW_SECONDS = 15 * 60;

type AccountRow = typeof accounts.$inferSelect;

function summary(row: AccountRow): AccountSummary {
  return {
    id: row.id,
    displayName: row.displayName,
    email: row.email,
    emailVerified: row.emailVerifiedAt !== null,
    status: row.status,
  };
}

function passwordProblem(problem: PasswordProblem): ProblemError {
  const messages: Record<PasswordProblem, string> = {
    too_short: 'Password must be at least 10 characters.',
    too_long: 'Password must be at most 128 characters.',
    contains_identity: 'Password must not contain your email or name.',
    breached: 'This password appeared in a data breach. Choose a different one.',
  };
  return ProblemError.badRequest(
    problem === 'breached' ? ErrorCodes.passwordBreached : ErrorCodes.passwordWeak,
    messages[problem],
    { reason: problem },
  );
}

export class AuthService {
  readonly sessions: SessionService;

  constructor(private readonly ctx: AppContext) {
    this.sessions = new SessionService(ctx);
  }

  // ---------------------------------------------------------------- validation helpers

  private requireDisplayName(name: string): void {
    const problem = checkDisplayName(name);
    if (problem) {
      throw ProblemError.badRequest(
        ErrorCodes.displayNameInvalid,
        problem === 'reserved' ? 'That name is reserved.' : DISPLAY_NAME_RULES,
        { reason: problem },
      );
    }
  }

  private async requireGoodPassword(password: string, identity: (string | null)[]): Promise<void> {
    const problem = checkPasswordPolicy(password, identity);
    if (problem) throw passwordProblem(problem);
    if (await this.ctx.breachChecker.isBreached(password)) throw passwordProblem('breached');
  }

  private mapUniqueViolation(err: unknown): never {
    const { code, constraint } = pgError(err);
    if (code === '23505' && constraint === 'uq_accounts_email') {
      throw ProblemError.conflict(ErrorCodes.emailTaken, 'An account with this email already exists.');
    }
    if (code === '23505' && constraint === 'uq_accounts_display_name_key') {
      throw ProblemError.conflict(ErrorCodes.displayNameTaken, 'That display name is taken.');
    }
    throw err;
  }

  // ---------------------------------------------------------------- email tokens

  private async createEmailToken(accountId: string, purpose: EmailTokenPurpose): Promise<string> {
    const token = randomToken();
    const ttl = purpose === 'verify' ? VERIFY_TTL_MS : RESET_TTL_MS;
    await this.ctx.db.insert(emailTokens).values({
      id: newId(),
      accountId,
      purpose,
      tokenHash: sha256(token),
      expiresAt: new Date(this.ctx.now().getTime() + ttl),
    });
    return token;
  }

  private link(path: string, token: string): string {
    return `${this.ctx.config.PUBLIC_BASE_URL.replace(/\/$/, '')}${path}?token=${token}`;
  }

  async sendVerificationEmail(account: { id: string; email: string; displayName: string }): Promise<void> {
    const token = await this.createEmailToken(account.id, 'verify');
    await this.ctx.mailer.send({
      to: account.email,
      subject: 'Confirm your KINDLING email address',
      text:
        `Hi ${account.displayName},\n\n` +
        `Confirm your email address by opening this link (valid for 48 hours):\n\n` +
        `${this.link('/account/verify-email', token)}\n\n` +
        `If you didn't create a KINDLING account, you can ignore this message.\n`,
    });
  }

  /** Marks a token used if it is valid, unused and unexpired. Returns the account id. */
  private async consumeEmailToken(token: string, purpose: EmailTokenPurpose): Promise<string> {
    const now = this.ctx.now();
    const [row] = await this.ctx.db
      .update(emailTokens)
      .set({ usedAt: now })
      .where(
        and(
          eq(emailTokens.tokenHash, sha256(token)),
          eq(emailTokens.purpose, purpose),
          isNull(emailTokens.usedAt),
          gt(emailTokens.expiresAt, now),
        ),
      )
      .returning({ accountId: emailTokens.accountId });
    if (!row) {
      throw ProblemError.badRequest(
        ErrorCodes.emailTokenInvalid,
        'This link is invalid, expired or already used.',
      );
    }
    return row.accountId;
  }

  // ---------------------------------------------------------------- flows

  async register(
    input: { email: string; password: string; displayName: string },
    meta: SessionMeta,
  ): Promise<AuthResult> {
    const email = normalizeEmail(input.email);
    if (!email) throw ProblemError.badRequest(ErrorCodes.emailInvalid, 'Enter a valid email address.');
    this.requireDisplayName(input.displayName);
    await this.requireGoodPassword(input.password, [email, input.displayName]);

    const passwordHash = await hashPassword(input.password);
    const id = newId();
    let row: AccountRow;
    try {
      row = await this.ctx.db.transaction(async (tx) => {
        const [created] = await tx
          .insert(accounts)
          .values({
            id,
            displayName: input.displayName,
            displayNameKey: displayNameKey(input.displayName),
            email,
            passwordHash,
            lastLoginAt: this.ctx.now(),
          })
          .returning();
        await tx.insert(profiles).values({ accountId: id });
        return created!;
      });
    } catch (err) {
      this.mapUniqueViolation(err);
    }

    await this.sendVerificationEmail({ id, email, displayName: input.displayName });
    const session = await this.sessions.start(id, meta);
    return { ...session, account: summary(row), created: true };
  }

  async login(input: { email: string; password: string }, meta: SessionMeta): Promise<AuthResult> {
    const email = normalizeEmail(input.email);
    const failKey = `auth:failed-login:${(email ?? input.email).toLowerCase()}`;
    const failures = Number((await this.ctx.redis.get(failKey).catch(() => null)) ?? 0);
    if (failures >= FAILED_LOGIN_LIMIT) {
      throw ProblemError.tooMany(
        ErrorCodes.tooManyAttempts,
        'Too many failed attempts. Try again in 15 minutes.',
      );
    }

    const [row] = email
      ? await this.ctx.db.select().from(accounts).where(eq(accounts.email, email)).limit(1)
      : [];
    const ok =
      row?.passwordHash && row.status !== 'deleted'
        ? await verifyPassword(row.passwordHash, input.password)
        : await verifyAgainstDummy(input.password);

    if (!ok || !row) {
      await this.ctx.redis
        .multi()
        .incr(failKey)
        .expire(failKey, FAILED_LOGIN_WINDOW_SECONDS)
        .exec()
        .catch(() => undefined);
      throw ProblemError.unauthorized(ErrorCodes.invalidCredentials, 'Email or password is incorrect.');
    }
    if (!canHoldSession(row.status)) {
      throw ProblemError.forbidden(ErrorCodes.accountSuspended, 'This account is suspended.');
    }

    await this.ctx.redis.del(failKey).catch(() => undefined);
    await this.ctx.db.update(accounts).set({ lastLoginAt: this.ctx.now() }).where(eq(accounts.id, row.id));
    const session = await this.sessions.start(row.id, meta);
    return { ...session, account: summary(row), created: false };
  }

  /**
   * Sign in with Apple. Existing identity → sign in. New identity → create an account, which needs a
   * display name (the first call without one returns 422 display_name_required and creates nothing).
   * Accounts are never linked automatically by email (that would allow takeover of unverified emails).
   */
  async signInWithApple(
    input: { identityToken: string; nonce: string; displayName?: string | undefined },
    meta: SessionMeta,
  ): Promise<AuthResult> {
    let identity;
    try {
      identity = await this.ctx.apple.verify(input.identityToken, input.nonce);
    } catch (err) {
      if (err instanceof AppleTokenError) {
        throw ProblemError.unauthorized(ErrorCodes.appleTokenInvalid, 'Apple sign-in could not be verified.');
      }
      throw err;
    }

    const [existing] = await this.ctx.db
      .select({ account: accounts })
      .from(authIdentities)
      .innerJoin(accounts, eq(accounts.id, authIdentities.accountId))
      .where(and(eq(authIdentities.provider, 'apple'), eq(authIdentities.subject, identity.subject)))
      .limit(1);

    if (existing) {
      const row = existing.account;
      if (!canHoldSession(row.status)) {
        throw ProblemError.forbidden(ErrorCodes.accountSuspended, 'This account is suspended.');
      }
      await this.ctx.db.update(accounts).set({ lastLoginAt: this.ctx.now() }).where(eq(accounts.id, row.id));
      const session = await this.sessions.start(row.id, meta);
      return { ...session, account: summary(row), created: false };
    }

    if (!input.displayName) {
      throw ProblemError.unprocessable(
        ErrorCodes.displayNameRequired,
        'Choose a display name to create your account.',
      );
    }
    this.requireDisplayName(input.displayName);

    // Use Apple's email only if Apple verified it and no other account already claims it.
    let email: string | null = identity.emailVerified ? normalizeEmail(identity.email ?? '') : null;
    if (email) {
      const [taken] = await this.ctx.db
        .select({ id: accounts.id })
        .from(accounts)
        .where(eq(accounts.email, email))
        .limit(1);
      if (taken) email = null;
    }

    const id = newId();
    let row: AccountRow;
    try {
      row = await this.ctx.db.transaction(async (tx) => {
        const [created] = await tx
          .insert(accounts)
          .values({
            id,
            displayName: input.displayName!,
            displayNameKey: displayNameKey(input.displayName!),
            email,
            emailVerifiedAt: email ? this.ctx.now() : null,
            lastLoginAt: this.ctx.now(),
          })
          .returning();
        await tx.insert(profiles).values({ accountId: id });
        await tx.insert(authIdentities).values({
          id: newId(),
          accountId: id,
          provider: 'apple',
          subject: identity.subject,
          email: identity.email,
        });
        return created!;
      });
    } catch (err) {
      const { code, constraint } = pgError(err);
      if (code === '23505' && constraint === 'uq_auth_identities_provider_subject') {
        // Two first-time sign-ins raced; the other one created the account.
        return this.signInWithApple({ identityToken: input.identityToken, nonce: input.nonce }, meta);
      }
      this.mapUniqueViolation(err);
    }

    const session = await this.sessions.start(id, meta);
    return { ...session, account: summary(row), created: true };
  }

  async verifyEmail(token: string): Promise<void> {
    const accountId = await this.consumeEmailToken(token, 'verify');
    await this.ctx.db
      .update(accounts)
      .set({ emailVerifiedAt: this.ctx.now(), updatedAt: this.ctx.now() })
      .where(and(eq(accounts.id, accountId), isNull(accounts.emailVerifiedAt)));
  }

  async resendVerification(accountId: string): Promise<'sent' | 'already_verified' | 'no_email'> {
    const [row] = await this.ctx.db.select().from(accounts).where(eq(accounts.id, accountId)).limit(1);
    if (!row) throw ProblemError.unauthorized();
    if (!row.email) return 'no_email';
    if (row.emailVerifiedAt) return 'already_verified';
    await this.sendVerificationEmail({ id: row.id, email: row.email, displayName: row.displayName });
    return 'sent';
  }

  /** Always succeeds from the caller's point of view, so it can't be used to discover accounts. */
  async forgotPassword(emailInput: string): Promise<void> {
    const email = normalizeEmail(emailInput);
    if (!email) return;
    const [row] = await this.ctx.db.select().from(accounts).where(eq(accounts.email, email)).limit(1);
    if (!row || !canHoldSession(row.status)) return;
    const token = await this.createEmailToken(row.id, 'reset');
    await this.ctx.mailer.send({
      to: email,
      subject: 'Reset your KINDLING password',
      text:
        `Hi ${row.displayName},\n\n` +
        `Someone asked to reset the password for your KINDLING account. If it was you, open this link (valid for 1 hour):\n\n` +
        `${this.link('/account/reset-password', token)}\n\n` +
        `If you didn't ask for this, ignore this message — your password stays the same.\n`,
    });
  }

  async resetPassword(token: string, newPassword: string): Promise<void> {
    // Validate the password before consuming the token, so a weak password doesn't burn the link.
    const tokenHash = sha256(token);
    const now = this.ctx.now();
    const [pending] = await this.ctx.db
      .select({ account: accounts })
      .from(emailTokens)
      .innerJoin(accounts, eq(accounts.id, emailTokens.accountId))
      .where(
        and(
          eq(emailTokens.tokenHash, tokenHash),
          eq(emailTokens.purpose, 'reset'),
          isNull(emailTokens.usedAt),
          gt(emailTokens.expiresAt, now),
        ),
      )
      .limit(1);
    if (!pending) {
      throw ProblemError.badRequest(
        ErrorCodes.emailTokenInvalid,
        'This link is invalid, expired or already used.',
      );
    }
    await this.requireGoodPassword(newPassword, [pending.account.email, pending.account.displayName]);

    const accountId = await this.consumeEmailToken(token, 'reset');
    await this.ctx.db
      .update(accounts)
      .set({
        passwordHash: await hashPassword(newPassword),
        // Receiving the reset email proves control of the inbox.
        emailVerifiedAt: pending.account.emailVerifiedAt ?? now,
        updatedAt: now,
      })
      .where(eq(accounts.id, accountId));
    await this.sessions.revokeAllForAccount(accountId, 'password_reset');
  }
}
