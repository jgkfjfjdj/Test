import { ErrorCodes } from '@kindling/contracts';
import { accounts, CLOUD_SAVE_SLOTS, cloudSaves, profiles, type CloudSaveSlot } from '@kindling/db';
import { and, eq, lt, sql } from 'drizzle-orm';
import type { AppContext } from '../../context.js';
import { pgError, ProblemError } from '../../lib/problem.js';
import { SessionService } from '../auth/sessions.js';
import { checkDisplayName, DISPLAY_NAME_RULES, displayNameKey } from './display-name.js';

/** Days between a deletion request and the permanent purge (the player can cancel meanwhile). */
export const DELETION_GRACE_DAYS = 7;
export const CLOUD_SAVE_MAX_BYTES = 64 * 1024;

export interface MeView {
  id: string;
  displayName: string;
  email: string | null;
  emailVerified: boolean;
  status: string;
  createdAt: string;
  deletion: { requestedAt: string; purgeAfter: string } | null;
  profile: { level: number; xp: number; loadout: Record<string, string> };
}

export function isCloudSaveSlot(value: string): value is CloudSaveSlot {
  return (CLOUD_SAVE_SLOTS as readonly string[]).includes(value);
}

export class AccountService {
  constructor(private readonly ctx: AppContext) {}

  private purgeAfter(requestedAt: Date): Date {
    return new Date(requestedAt.getTime() + DELETION_GRACE_DAYS * 86_400_000);
  }

  async me(accountId: string): Promise<MeView> {
    const [row] = await this.ctx.db
      .select({ account: accounts, profile: profiles })
      .from(accounts)
      .leftJoin(profiles, eq(profiles.accountId, accounts.id))
      .where(eq(accounts.id, accountId))
      .limit(1);
    if (!row) throw ProblemError.unauthorized();
    const a = row.account;
    return {
      id: a.id,
      displayName: a.displayName,
      email: a.email,
      emailVerified: a.emailVerifiedAt !== null,
      status: a.status,
      createdAt: a.createdAt.toISOString(),
      deletion:
        a.status === 'pending_deletion' && a.deletionRequestedAt
          ? {
              requestedAt: a.deletionRequestedAt.toISOString(),
              purgeAfter: this.purgeAfter(a.deletionRequestedAt).toISOString(),
            }
          : null,
      profile: {
        level: row.profile?.level ?? 1,
        xp: row.profile?.xp ?? 0,
        loadout: row.profile?.loadout ?? {},
      },
    };
  }

  async rename(accountId: string, displayName: string): Promise<MeView> {
    const problem = checkDisplayName(displayName);
    if (problem) {
      throw ProblemError.badRequest(
        ErrorCodes.displayNameInvalid,
        problem === 'reserved' ? 'That name is reserved.' : DISPLAY_NAME_RULES,
        { reason: problem },
      );
    }
    try {
      await this.ctx.db
        .update(accounts)
        .set({ displayName, displayNameKey: displayNameKey(displayName), updatedAt: this.ctx.now() })
        .where(eq(accounts.id, accountId));
    } catch (err) {
      const { code, constraint } = pgError(err);
      if (code === '23505' && constraint === 'uq_accounts_display_name_key') {
        throw ProblemError.conflict(ErrorCodes.displayNameTaken, 'That display name is taken.');
      }
      throw err;
    }
    return this.me(accountId);
  }

  /** Starts the deletion grace period and signs the account out everywhere. */
  async requestDeletion(accountId: string): Promise<{ purgeAfter: string }> {
    const now = this.ctx.now();
    const [row] = await this.ctx.db
      .update(accounts)
      .set({ status: 'pending_deletion', deletionRequestedAt: now, updatedAt: now })
      .where(and(eq(accounts.id, accountId), sql`${accounts.status} IN ('active', 'pending_deletion')`))
      .returning({ requestedAt: accounts.deletionRequestedAt });
    if (!row) throw ProblemError.forbidden(ErrorCodes.accountSuspended);
    await new SessionService(this.ctx).revokeAllForAccount(accountId, 'account_deletion');
    return { purgeAfter: this.purgeAfter(row.requestedAt ?? now).toISOString() };
  }

  async cancelDeletion(accountId: string): Promise<MeView> {
    const updated = await this.ctx.db
      .update(accounts)
      .set({ status: 'active', deletionRequestedAt: null, updatedAt: this.ctx.now() })
      .where(and(eq(accounts.id, accountId), eq(accounts.status, 'pending_deletion')))
      .returning({ id: accounts.id });
    if (updated.length === 0) {
      throw ProblemError.conflict(
        ErrorCodes.deletionNotPending,
        'This account is not scheduled for deletion.',
      );
    }
    return this.me(accountId);
  }

  /**
   * Permanently deletes accounts whose grace period has ended. Rows owned by the account
   * (identities, sessions, profile, cloud saves, play sessions) go with it via ON DELETE CASCADE;
   * servers it owned keep running without an owner (owner_account_id → NULL).
   * Run daily (scheduled job — see the M2 status report); returns the number purged.
   */
  async purgeExpiredDeletions(limit = 500): Promise<number> {
    const cutoff = new Date(this.ctx.now().getTime() - DELETION_GRACE_DAYS * 86_400_000);
    const due = await this.ctx.db
      .select({ id: accounts.id })
      .from(accounts)
      .where(and(eq(accounts.status, 'pending_deletion'), lt(accounts.deletionRequestedAt, cutoff)))
      .limit(limit);
    let purged = 0;
    for (const { id } of due) {
      const deleted = await this.ctx.db
        .delete(accounts)
        .where(and(eq(accounts.id, id), eq(accounts.status, 'pending_deletion')))
        .returning({ id: accounts.id });
      purged += deleted.length;
    }
    return purged;
  }

  // ---------------------------------------------------------------- cloud saves

  async getCloudSave(accountId: string, slot: CloudSaveSlot) {
    const [row] = await this.ctx.db
      .select()
      .from(cloudSaves)
      .where(and(eq(cloudSaves.accountId, accountId), eq(cloudSaves.slot, slot)))
      .limit(1);
    if (!row) throw ProblemError.notFound('No cloud save in this slot yet.', ErrorCodes.cloudSaveNotFound);
    return { slot, version: row.version, data: row.data, updatedAt: row.updatedAt.toISOString() };
  }

  /**
   * Optimistic concurrency: the write must name the version it was based on (0 for a new slot).
   * On mismatch the current server copy is returned in the 409 body so the client can merge.
   */
  async putCloudSave(accountId: string, slot: CloudSaveSlot, baseVersion: number, data: unknown) {
    const size = Buffer.byteLength(JSON.stringify(data));
    if (size > CLOUD_SAVE_MAX_BYTES) {
      throw new ProblemError(
        413,
        ErrorCodes.cloudSaveTooLarge,
        'Payload Too Large',
        `Cloud saves are limited to ${CLOUD_SAVE_MAX_BYTES} bytes.`,
      );
    }
    const now = this.ctx.now();
    let rows;
    if (baseVersion === 0) {
      rows = await this.ctx.db
        .insert(cloudSaves)
        .values({ accountId, slot, version: 1, data, updatedAt: now })
        .onConflictDoNothing()
        .returning();
    } else {
      rows = await this.ctx.db
        .update(cloudSaves)
        .set({ version: baseVersion + 1, data, updatedAt: now })
        .where(
          and(
            eq(cloudSaves.accountId, accountId),
            eq(cloudSaves.slot, slot),
            eq(cloudSaves.version, baseVersion),
          ),
        )
        .returning();
    }
    const saved = rows[0];
    if (!saved) {
      const current = await this.getCloudSave(accountId, slot).catch(() => null);
      throw ProblemError.conflict(ErrorCodes.cloudSaveConflict, 'The cloud save changed on another device.', {
        current,
      });
    }
    return { slot, version: saved.version, data: saved.data, updatedAt: saved.updatedAt.toISOString() };
  }
}
