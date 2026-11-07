import { ErrorCodes, isPermission, type Permission } from '@kindling/contracts';
import { newId, staffRoles, staffSessions, staffUserRoles, staffUsers, type StaffStatus } from '@kindling/db';
import { and, eq, gt, inArray, isNull } from 'drizzle-orm';
import { createHmac } from 'node:crypto';
import type { AppContext } from '../../context.js';
import { constantTimeEqual, decryptSecret, encryptSecret, randomToken, sha256 } from '../../lib/crypto.js';
import {
  checkPasswordPolicy,
  hashPassword,
  verifyAgainstDummy,
  verifyPassword,
} from '../../lib/passwords.js';
import { pgError, ProblemError } from '../../lib/problem.js';
import { base32Encode, generateTotpSecret, otpauthUri, verifyTotp } from '../../lib/totp.js';
import { normalizeEmail } from '../auth/email.js';

export const STAFF_COOKIE = 'kindling_staff';
export const STEP_UP_WINDOW_MS = 5 * 60_000;
const SETUP_TTL_MS = 72 * 3600_000;
const FAILED_LIMIT = 5;
const FAILED_WINDOW_SECONDS = 15 * 60;

export interface StaffIdentity {
  id: string;
  email: string;
  name: string;
  roles: string[];
  permissions: Set<Permission>;
  sessionId: string;
  stepUpAt: Date | null;
}

export interface StaffView {
  id: string;
  email: string;
  name: string;
  status: StaffStatus;
  roles: string[];
  lastLoginAt: string | null;
  createdAt: string;
}

/** CSRF token derived from the session token (so it never needs storing in plaintext). */
export function csrfFor(sessionToken: string): string {
  return createHmac('sha256', sessionToken).update('kindling-staff-csrf').digest('base64url');
}

export class StaffService {
  constructor(private readonly ctx: AppContext) {}

  // ---------------------------------------------------------------- roles

  async rolesOf(staffId: string): Promise<{ roles: string[]; permissions: Set<Permission> }> {
    const rows = await this.ctx.db
      .select({ id: staffRoles.id, permissions: staffRoles.permissions })
      .from(staffUserRoles)
      .innerJoin(staffRoles, eq(staffRoles.id, staffUserRoles.roleId))
      .where(eq(staffUserRoles.staffId, staffId));
    const permissions = new Set<Permission>();
    for (const row of rows) for (const p of row.permissions) if (isPermission(p)) permissions.add(p);
    return { roles: rows.map((r) => r.id).sort(), permissions };
  }

  async listRoles() {
    return this.ctx.db.select().from(staffRoles).orderBy(staffRoles.id);
  }

  private async requireRoles(roleIds: string[]): Promise<void> {
    if (roleIds.length === 0) return;
    const rows = await this.ctx.db
      .select({ id: staffRoles.id })
      .from(staffRoles)
      .where(inArray(staffRoles.id, roleIds));
    const known = new Set(rows.map((r) => r.id));
    const unknown = roleIds.filter((r) => !known.has(r));
    if (unknown.length)
      throw ProblemError.badRequest(ErrorCodes.staffUnknownRole, `Unknown role(s): ${unknown.join(', ')}`);
  }

  // ---------------------------------------------------------------- invitations and setup

  /** Creates an invited staff user and returns the one-time setup token (valid 72 hours). */
  async invite(input: {
    email: string;
    name: string;
    roles: string[];
  }): Promise<{ id: string; setupToken: string }> {
    const email = normalizeEmail(input.email);
    if (!email) throw ProblemError.badRequest(ErrorCodes.emailInvalid, 'Enter a valid email address.');
    await this.requireRoles(input.roles);
    const id = newId();
    const setupToken = randomToken();
    try {
      await this.ctx.db.transaction(async (tx) => {
        await tx.insert(staffUsers).values({
          id,
          email,
          name: input.name.trim(),
          status: 'invited',
          setupTokenHash: sha256(setupToken),
          setupExpiresAt: new Date(this.ctx.now().getTime() + SETUP_TTL_MS),
        });
        if (input.roles.length)
          await tx.insert(staffUserRoles).values(input.roles.map((roleId) => ({ staffId: id, roleId })));
      });
    } catch (err) {
      const { code, constraint } = pgError(err);
      if (code === '23505' && constraint === 'uq_staff_users_email') {
        throw ProblemError.conflict(ErrorCodes.staffEmailTaken, 'A staff user with this email exists.');
      }
      throw err;
    }
    return { id, setupToken };
  }

  setupUrl(token: string): string {
    return `${this.ctx.config.PUBLIC_BASE_URL.replace(/\/$/, '')}/admin/setup?token=${token}`;
  }

  async sendInvite(email: string, name: string, setupToken: string): Promise<void> {
    await this.ctx.mailer.send({
      to: email,
      subject: 'Your KINDLING staff console invitation',
      text:
        `Hi ${name},\n\nYou were invited to the KINDLING staff console. Finish setting up your account ` +
        `(password + authenticator app) within 72 hours:\n\n${this.setupUrl(setupToken)}\n`,
    });
  }

  private async invitedBySetupToken(token: string) {
    const [row] = await this.ctx.db
      .select()
      .from(staffUsers)
      .where(
        and(
          eq(staffUsers.setupTokenHash, sha256(token)),
          eq(staffUsers.status, 'invited'),
          gt(staffUsers.setupExpiresAt, this.ctx.now()),
        ),
      )
      .limit(1);
    if (!row)
      throw ProblemError.badRequest(ErrorCodes.staffSetupInvalid, 'This setup link is invalid or expired.');
    return row;
  }

  /** Step 1: generate a TOTP secret for the invited user's authenticator app. */
  async beginSetup(token: string): Promise<{ email: string; secret: string; otpauthUri: string }> {
    const row = await this.invitedBySetupToken(token);
    const secret = generateTotpSecret();
    await this.ctx.db
      .update(staffUsers)
      .set({ totpSecretEnc: encryptSecret(this.ctx.staffSecretKey, secret), updatedAt: this.ctx.now() })
      .where(eq(staffUsers.id, row.id));
    return { email: row.email, secret: base32Encode(secret), otpauthUri: otpauthUri(secret, row.email) };
  }

  /** Step 2: set the password and prove the authenticator works. Activates the account. */
  async completeSetup(token: string, password: string, totp: string): Promise<{ id: string }> {
    const row = await this.invitedBySetupToken(token);
    if (!row.totpSecretEnc)
      throw ProblemError.badRequest(ErrorCodes.staffSetupInvalid, 'Start the authenticator setup first.');
    const problem = checkPasswordPolicy(password, [row.email, row.name]);
    if (problem)
      throw ProblemError.badRequest(ErrorCodes.passwordWeak, 'Password does not meet the policy.', {
        reason: problem,
      });
    if (password.length < 14) {
      throw ProblemError.badRequest(ErrorCodes.passwordWeak, 'Staff passwords need at least 14 characters.', {
        reason: 'too_short',
      });
    }
    if (await this.ctx.breachChecker.isBreached(password)) {
      throw ProblemError.badRequest(ErrorCodes.passwordBreached, 'This password appeared in a data breach.');
    }
    const secret = decryptSecret(this.ctx.staffSecretKey, row.totpSecretEnc);
    const step = verifyTotp(secret, totp, Math.floor(this.ctx.now().getTime() / 1000));
    if (step === null)
      throw ProblemError.badRequest(
        ErrorCodes.staffInvalidCredentials,
        'The authenticator code is not valid.',
      );
    await this.rememberTotpStep(row.id, step);
    await this.ctx.db
      .update(staffUsers)
      .set({
        passwordHash: await hashPassword(password),
        status: 'active',
        setupTokenHash: null,
        setupExpiresAt: null,
        updatedAt: this.ctx.now(),
      })
      .where(eq(staffUsers.id, row.id));
    return { id: row.id };
  }

  // ---------------------------------------------------------------- TOTP replay protection

  /** Each 30-second code can be used once: the newest accepted step is remembered for 2 minutes. */
  private async rememberTotpStep(staffId: string, step: number): Promise<void> {
    await this.ctx.redis.set(`staff:totp-step:${staffId}`, String(step), 'EX', 120).catch(() => undefined);
  }

  private async checkTotp(staffId: string, secretEnc: Buffer | null, code: string): Promise<boolean> {
    if (!secretEnc) return false;
    const secret = decryptSecret(this.ctx.staffSecretKey, secretEnc);
    const step = verifyTotp(secret, code, Math.floor(this.ctx.now().getTime() / 1000));
    if (step === null) return false;
    const last = Number((await this.ctx.redis.get(`staff:totp-step:${staffId}`).catch(() => null)) ?? -1);
    if (step <= last) return false; // replayed or older code
    await this.rememberTotpStep(staffId, step);
    return true;
  }

  // ---------------------------------------------------------------- sessions

  async login(
    input: { email: string; password: string; totp: string },
    meta: { ip: string; userAgent?: string | undefined },
  ): Promise<{ token: string; csrfToken: string; expiresAt: Date; staffId: string }> {
    const email = normalizeEmail(input.email) ?? input.email;
    const failKey = `staff:failed-login:${email.toLowerCase()}`;
    const failures = Number((await this.ctx.redis.get(failKey).catch(() => null)) ?? 0);
    if (failures >= FAILED_LIMIT) {
      throw ProblemError.tooMany(
        ErrorCodes.tooManyAttempts,
        'Too many failed attempts. Try again in 15 minutes.',
      );
    }
    const [row] = await this.ctx.db.select().from(staffUsers).where(eq(staffUsers.email, email)).limit(1);
    const passwordOk =
      row?.passwordHash && row.status === 'active'
        ? await verifyPassword(row.passwordHash, input.password)
        : await verifyAgainstDummy(input.password);
    const ok = passwordOk && row ? await this.checkTotp(row.id, row.totpSecretEnc, input.totp) : false;
    if (!ok || !row) {
      await this.ctx.redis
        .multi()
        .incr(failKey)
        .expire(failKey, FAILED_WINDOW_SECONDS)
        .exec()
        .catch(() => undefined);
      throw ProblemError.unauthorized(
        ErrorCodes.staffInvalidCredentials,
        'Email, password or code is incorrect.',
      );
    }
    await this.ctx.redis.del(failKey).catch(() => undefined);

    const now = this.ctx.now();
    const token = randomToken();
    const csrfToken = csrfFor(token);
    const expiresAt = new Date(now.getTime() + this.ctx.config.STAFF_SESSION_HOURS * 3600_000);
    await this.ctx.db.insert(staffSessions).values({
      id: newId(),
      staffId: row.id,
      tokenHash: sha256(token),
      csrfHash: sha256(csrfToken),
      // Signing in with password + TOTP counts as a fresh step-up.
      stepUpAt: now,
      expiresAt,
      lastSeenAt: now,
      ipHash: this.ctx.hashIp(meta.ip),
      userAgent: meta.userAgent?.slice(0, 256) ?? null,
    });
    await this.ctx.db.update(staffUsers).set({ lastLoginAt: now }).where(eq(staffUsers.id, row.id));
    return { token, csrfToken, expiresAt, staffId: row.id };
  }

  /** Resolves a session cookie to a staff identity, enforcing absolute and idle expiry. */
  async authenticate(token: string): Promise<StaffIdentity | null> {
    const now = this.ctx.now();
    const [row] = await this.ctx.db
      .select({ session: staffSessions, staff: staffUsers })
      .from(staffSessions)
      .innerJoin(staffUsers, eq(staffUsers.id, staffSessions.staffId))
      .where(and(eq(staffSessions.tokenHash, sha256(token)), isNull(staffSessions.revokedAt)))
      .limit(1);
    if (!row || row.staff.status !== 'active') return null;
    const idleLimit = this.ctx.config.STAFF_IDLE_MINUTES * 60_000;
    if (row.session.expiresAt <= now || now.getTime() - row.session.lastSeenAt.getTime() > idleLimit) {
      await this.revokeSession(row.session.id);
      return null;
    }
    if (now.getTime() - row.session.lastSeenAt.getTime() > 60_000) {
      await this.ctx.db
        .update(staffSessions)
        .set({ lastSeenAt: now })
        .where(eq(staffSessions.id, row.session.id));
    }
    const { roles, permissions } = await this.rolesOf(row.staff.id);
    return {
      id: row.staff.id,
      email: row.staff.email,
      name: row.staff.name,
      roles,
      permissions,
      sessionId: row.session.id,
      stepUpAt: row.session.stepUpAt,
    };
  }

  verifyCsrf(sessionToken: string, header: string | undefined): boolean {
    if (!header) return false;
    return constantTimeEqual(Buffer.from(csrfFor(sessionToken)), Buffer.from(header));
  }

  async stepUp(staffId: string, sessionId: string, totp: string): Promise<Date> {
    const [row] = await this.ctx.db.select().from(staffUsers).where(eq(staffUsers.id, staffId)).limit(1);
    if (!row || !(await this.checkTotp(staffId, row.totpSecretEnc, totp))) {
      throw ProblemError.unauthorized(
        ErrorCodes.staffInvalidCredentials,
        'The authenticator code is not valid.',
      );
    }
    const now = this.ctx.now();
    await this.ctx.db.update(staffSessions).set({ stepUpAt: now }).where(eq(staffSessions.id, sessionId));
    return new Date(now.getTime() + STEP_UP_WINDOW_MS);
  }

  async revokeSession(sessionId: string): Promise<void> {
    await this.ctx.db
      .update(staffSessions)
      .set({ revokedAt: this.ctx.now() })
      .where(and(eq(staffSessions.id, sessionId), isNull(staffSessions.revokedAt)));
  }

  async revokeAllSessions(staffId: string): Promise<number> {
    const rows = await this.ctx.db
      .update(staffSessions)
      .set({ revokedAt: this.ctx.now() })
      .where(and(eq(staffSessions.staffId, staffId), isNull(staffSessions.revokedAt)))
      .returning({ id: staffSessions.id });
    return rows.length;
  }

  // ---------------------------------------------------------------- management

  async view(staffId: string): Promise<StaffView | null> {
    const [row] = await this.ctx.db.select().from(staffUsers).where(eq(staffUsers.id, staffId)).limit(1);
    if (!row) return null;
    const { roles } = await this.rolesOf(row.id);
    return {
      id: row.id,
      email: row.email,
      name: row.name,
      status: row.status,
      roles,
      lastLoginAt: row.lastLoginAt?.toISOString() ?? null,
      createdAt: row.createdAt.toISOString(),
    };
  }

  async list(): Promise<StaffView[]> {
    const rows = await this.ctx.db.select({ id: staffUsers.id }).from(staffUsers).orderBy(staffUsers.email);
    const views = await Promise.all(rows.map((r) => this.view(r.id)));
    return views.filter((v): v is StaffView => v !== null);
  }

  async update(
    staffId: string,
    changes: {
      name?: string | undefined;
      roles?: string[] | undefined;
      status?: 'active' | 'disabled' | undefined;
    },
  ) {
    const before = await this.view(staffId);
    if (!before) throw ProblemError.notFound('Staff user not found.');
    if (changes.status === 'active' && before.status === 'invited') {
      throw ProblemError.badRequest(
        ErrorCodes.staffSetupInvalid,
        'Invited users activate themselves by finishing setup.',
      );
    }
    if (changes.roles) await this.requireRoles(changes.roles);
    await this.ctx.db.transaction(async (tx) => {
      const set: Partial<typeof staffUsers.$inferInsert> = { updatedAt: this.ctx.now() };
      if (changes.name) set.name = changes.name.trim();
      if (changes.status) set.status = changes.status;
      await tx.update(staffUsers).set(set).where(eq(staffUsers.id, staffId));
      if (changes.roles) {
        await tx.delete(staffUserRoles).where(eq(staffUserRoles.staffId, staffId));
        if (changes.roles.length)
          await tx.insert(staffUserRoles).values(changes.roles.map((roleId) => ({ staffId, roleId })));
      }
    });
    if (changes.status === 'disabled') await this.revokeAllSessions(staffId);
    return { before, after: (await this.view(staffId))! };
  }

  /** Lost authenticator: back to invited with a fresh setup link; all sessions end. */
  async resetCredentials(staffId: string): Promise<{ setupToken: string }> {
    const setupToken = randomToken();
    const updated = await this.ctx.db
      .update(staffUsers)
      .set({
        status: 'invited',
        passwordHash: null,
        totpSecretEnc: null,
        setupTokenHash: sha256(setupToken),
        setupExpiresAt: new Date(this.ctx.now().getTime() + SETUP_TTL_MS),
        updatedAt: this.ctx.now(),
      })
      .where(eq(staffUsers.id, staffId))
      .returning({ id: staffUsers.id });
    if (updated.length === 0) throw ProblemError.notFound('Staff user not found.');
    await this.revokeAllSessions(staffId);
    return { setupToken };
  }
}
