import { sql } from 'drizzle-orm';
import { check, index, pgTable, primaryKey, text, unique, uuid } from 'drizzle-orm/pg-core';
import { bytea, citext, createdAt, timestamptz, updatedAt } from './types.js';

export const STAFF_STATUSES = ['invited', 'active', 'disabled'] as const;
export type StaffStatus = (typeof STAFF_STATUSES)[number];

/** Studio staff — a separate identity system from player accounts. */
export const staffUsers = pgTable(
  'staff_users',
  {
    id: uuid('id').primaryKey(),
    email: citext('email').notNull(),
    name: text('name').notNull(),
    passwordHash: text('password_hash'),
    /** TOTP secret encrypted with AES-256-GCM (STAFF_SECRET_KEY). */
    totpSecretEnc: bytea('totp_secret_enc'),
    status: text('status').$type<StaffStatus>().notNull().default('invited'),
    /** One-time setup token (SHA-256) for invited users. */
    setupTokenHash: bytea('setup_token_hash'),
    setupExpiresAt: timestamptz('setup_expires_at'),
    lastLoginAt: timestamptz('last_login_at'),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
  },
  (t) => [
    unique('uq_staff_users_email').on(t.email),
    unique('uq_staff_users_setup_token').on(t.setupTokenHash),
    check('ck_staff_users_status', sql`${t.status} IN ('invited','active','disabled')`),
  ],
);

export const staffRoles = pgTable('staff_roles', {
  id: text('id').primaryKey(),
  name: text('name').notNull(),
  permissions: text('permissions').array().notNull(),
});

export const staffUserRoles = pgTable(
  'staff_user_roles',
  {
    staffId: uuid('staff_id')
      .notNull()
      .references(() => staffUsers.id, { onDelete: 'cascade' }),
    roleId: text('role_id')
      .notNull()
      .references(() => staffRoles.id),
  },
  (t) => [primaryKey({ name: 'pk_staff_user_roles', columns: [t.staffId, t.roleId] })],
);

export const staffSessions = pgTable(
  'staff_sessions',
  {
    id: uuid('id').primaryKey(),
    staffId: uuid('staff_id')
      .notNull()
      .references(() => staffUsers.id, { onDelete: 'cascade' }),
    tokenHash: bytea('token_hash').notNull(),
    csrfHash: bytea('csrf_hash').notNull(),
    stepUpAt: timestamptz('step_up_at'),
    expiresAt: timestamptz('expires_at').notNull(),
    lastSeenAt: timestamptz('last_seen_at').notNull(),
    ipHash: bytea('ip_hash'),
    userAgent: text('user_agent'),
    revokedAt: timestamptz('revoked_at'),
    createdAt: createdAt(),
  },
  (t) => [
    unique('uq_staff_sessions_token').on(t.tokenHash),
    index('ix_staff_sessions_staff_active')
      .on(t.staffId)
      .where(sql`${t.revokedAt} IS NULL`),
  ],
);
