import { sql } from 'drizzle-orm';
import { check, index, integer, jsonb, pgTable, text, uuid } from 'drizzle-orm/pg-core';
import { createdAt, timestamptz, updatedAt } from './types.js';

/** One row per platform: builds below min_supported_build must update; below latest_build may update. */
export const versionPolicy = pgTable('version_policy', {
  platform: text('platform').primaryKey(),
  minSupportedBuild: integer('min_supported_build').notNull(),
  latestBuild: integer('latest_build').notNull(),
  storeUrl: text('store_url').notNull(),
  updatedBy: uuid('updated_by'),
  updatedAt: updatedAt(),
});

export const MAINTENANCE_SCOPES = ['full', 'login', 'matchmaking', 'store'] as const;
export type MaintenanceScope = (typeof MAINTENANCE_SCOPES)[number];

export const maintenanceWindows = pgTable(
  'maintenance_windows',
  {
    id: uuid('id').primaryKey(),
    scope: text('scope').$type<MaintenanceScope>().notNull(),
    startsAt: timestamptz('starts_at').notNull(),
    endsAt: timestamptz('ends_at'),
    /** Localized message: { "en": "..." }. */
    message: jsonb('message').$type<Record<string, string>>().notNull(),
    createdBy: uuid('created_by'),
    cancelledAt: timestamptz('cancelled_at'),
    createdAt: createdAt(),
  },
  (t) => [
    index('ix_maintenance_windows_active')
      .on(t.startsAt)
      .where(sql`${t.cancelledAt} IS NULL`),
    check('ck_maintenance_scope', sql`${t.scope} IN ('full','login','matchmaking','store')`),
  ],
);

export const REMOTE_CONFIG_SCOPES = ['client', 'server', 'both'] as const;
export type RemoteConfigScope = (typeof REMOTE_CONFIG_SCOPES)[number];

/** Remote config entries. Targeting rules arrive in M9; M2 serves plain key → value. */
export const remoteConfig = pgTable(
  'remote_config',
  {
    key: text('key').primaryKey(),
    value: jsonb('value').$type<unknown>().notNull(),
    scope: text('scope').$type<RemoteConfigScope>().notNull().default('client'),
    description: text('description'),
    version: integer('version').notNull().default(1),
    updatedBy: uuid('updated_by'),
    updatedAt: updatedAt(),
  },
  (t) => [check('ck_remote_config_scope', sql`${t.scope} IN ('client','server','both')`)],
);
