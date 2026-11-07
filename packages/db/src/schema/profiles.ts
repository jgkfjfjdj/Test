import { sql } from 'drizzle-orm';
import { bigint, check, integer, jsonb, pgTable, primaryKey, text, uuid } from 'drizzle-orm/pg-core';
import { accounts } from './identity.js';
import { updatedAt } from './types.js';

export const profiles = pgTable('profiles', {
  accountId: uuid('account_id')
    .primaryKey()
    .references(() => accounts.id, { onDelete: 'cascade' }),
  level: integer('level').notNull().default(1),
  xp: bigint('xp', { mode: 'number' }).notNull().default(0),
  /** Equipped cosmetic item keys by slot. */
  loadout: jsonb('loadout').$type<Record<string, string>>().notNull().default({}),
  /** Who may send friend requests / join (M7). */
  privacy: jsonb('privacy').$type<Record<string, unknown>>().notNull().default({}),
  updatedAt: updatedAt(),
});

export const CLOUD_SAVE_SLOTS = ['settings', 'hud', 'loadout', 'tutorial'] as const;
export type CloudSaveSlot = (typeof CLOUD_SAVE_SLOTS)[number];

export const cloudSaves = pgTable(
  'cloud_saves',
  {
    accountId: uuid('account_id')
      .notNull()
      .references(() => accounts.id, { onDelete: 'cascade' }),
    slot: text('slot').$type<CloudSaveSlot>().notNull(),
    version: integer('version').notNull(),
    data: jsonb('data').$type<unknown>().notNull(),
    updatedAt: updatedAt(),
  },
  (t) => [
    primaryKey({ name: 'pk_cloud_saves', columns: [t.accountId, t.slot] }),
    check('ck_cloud_saves_slot', sql`${t.slot} IN ('settings','hud','loadout','tutorial')`),
  ],
);
