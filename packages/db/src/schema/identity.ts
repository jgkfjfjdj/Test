import { sql } from 'drizzle-orm';
import { check, index, pgTable, text, unique, uuid } from 'drizzle-orm/pg-core';
import { bytea, citext, createdAt, timestamptz, updatedAt } from './types.js';

export const ACCOUNT_STATUSES = ['active', 'suspended', 'pending_deletion', 'deleted'] as const;
export type AccountStatus = (typeof ACCOUNT_STATUSES)[number];

export const accounts = pgTable(
  'accounts',
  {
    id: uuid('id').primaryKey(),
    displayName: text('display_name').notNull(),
    /** Lower-cased display name; enforces case-insensitive uniqueness. */
    displayNameKey: text('display_name_key').notNull(),
    email: citext('email'),
    emailVerifiedAt: timestamptz('email_verified_at'),
    passwordHash: text('password_hash'),
    status: text('status').$type<AccountStatus>().notNull().default('active'),
    deletionRequestedAt: timestamptz('deletion_requested_at'),
    lastLoginAt: timestamptz('last_login_at'),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
  },
  (t) => [
    unique('uq_accounts_display_name_key').on(t.displayNameKey),
    unique('uq_accounts_email').on(t.email),
    check('ck_accounts_status', sql`${t.status} IN ('active','suspended','pending_deletion','deleted')`),
  ],
);

export const AUTH_PROVIDERS = ['apple'] as const;
export type AuthProvider = (typeof AUTH_PROVIDERS)[number];

/** External sign-in identities (Sign in with Apple; Game Center later). */
export const authIdentities = pgTable(
  'auth_identities',
  {
    id: uuid('id').primaryKey(),
    accountId: uuid('account_id')
      .notNull()
      .references(() => accounts.id, { onDelete: 'cascade' }),
    provider: text('provider').$type<AuthProvider>().notNull(),
    subject: text('subject').notNull(),
    email: text('email'),
    createdAt: createdAt(),
  },
  (t) => [
    unique('uq_auth_identities_provider_subject').on(t.provider, t.subject),
    index('ix_auth_identities_account').on(t.accountId),
    check('ck_auth_identities_provider', sql`${t.provider} IN ('apple')`),
  ],
);

/**
 * Rotating refresh tokens. Each login starts a family; every refresh replaces the token
 * (rotated_at set on the old one). Presenting an already-rotated token revokes the family.
 */
export const refreshTokens = pgTable(
  'refresh_tokens',
  {
    id: uuid('id').primaryKey(),
    accountId: uuid('account_id')
      .notNull()
      .references(() => accounts.id, { onDelete: 'cascade' }),
    familyId: uuid('family_id').notNull(),
    tokenHash: bytea('token_hash').notNull(),
    expiresAt: timestamptz('expires_at').notNull(),
    rotatedAt: timestamptz('rotated_at'),
    revokedAt: timestamptz('revoked_at'),
    revokeReason: text('revoke_reason'),
    clientVersion: text('client_version'),
    userAgent: text('user_agent'),
    createdIpHash: bytea('created_ip_hash'),
    createdAt: createdAt(),
  },
  (t) => [
    unique('uq_refresh_tokens_hash').on(t.tokenHash),
    index('ix_refresh_tokens_family').on(t.familyId),
    index('ix_refresh_tokens_account_active')
      .on(t.accountId)
      .where(sql`${t.revokedAt} IS NULL`),
  ],
);

export const EMAIL_TOKEN_PURPOSES = ['verify', 'reset'] as const;
export type EmailTokenPurpose = (typeof EMAIL_TOKEN_PURPOSES)[number];

export const emailTokens = pgTable(
  'email_tokens',
  {
    id: uuid('id').primaryKey(),
    accountId: uuid('account_id')
      .notNull()
      .references(() => accounts.id, { onDelete: 'cascade' }),
    purpose: text('purpose').$type<EmailTokenPurpose>().notNull(),
    tokenHash: bytea('token_hash').notNull(),
    expiresAt: timestamptz('expires_at').notNull(),
    usedAt: timestamptz('used_at'),
    createdAt: createdAt(),
  },
  (t) => [
    unique('uq_email_tokens_hash').on(t.tokenHash),
    index('ix_email_tokens_account').on(t.accountId, t.purpose),
    check('ck_email_tokens_purpose', sql`${t.purpose} IN ('verify','reset')`),
  ],
);
