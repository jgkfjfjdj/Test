import { sql } from 'drizzle-orm';
import { check, index, integer, jsonb, pgTable, text, unique, uuid } from 'drizzle-orm/pg-core';
import { accounts } from './identity.js';
import { bytea, createdAt, timestamptz, updatedAt } from './types.js';

export const SERVER_KINDS = ['official', 'community'] as const;
export type ServerKind = (typeof SERVER_KINDS)[number];
export const SERVER_LISTINGS = ['unlisted', 'listed', 'verified', 'delisted'] as const;
export type ServerListing = (typeof SERVER_LISTINGS)[number];
export const SERVER_MODES = ['pvp', 'pve'] as const;
export type ServerMode = (typeof SERVER_MODES)[number];

export const gameServers = pgTable(
  'game_servers',
  {
    id: uuid('id').primaryKey(),
    kind: text('kind').$type<ServerKind>().notNull(),
    ownerAccountId: uuid('owner_account_id').references(() => accounts.id, { onDelete: 'set null' }),
    name: text('name').notNull(),
    description: text('description').notNull().default(''),
    region: text('region').notNull(),
    /** Public host (DNS name preferred, for NAT64) and ports, as reported by the server's hello. */
    host: text('host'),
    gamePort: integer('game_port'),
    queryPort: integer('query_port'),
    maxPlayers: integer('max_players').notNull().default(50),
    /** argon2id hash of the join password (from hello); null = public. */
    passwordHash: text('password_hash'),
    mapId: text('map_id'),
    mode: text('mode').$type<ServerMode>().notNull().default('pvp'),
    tags: text('tags')
      .array()
      .notNull()
      .default(sql`'{}'::text[]`),
    listing: text('listing').$type<ServerListing>().notNull().default('unlisted'),
    delistReason: text('delist_reason'),
    buildVersion: text('build_version'),
    protocolVersion: integer('protocol_version'),
    lastWipeAt: timestamptz('last_wipe_at'),
    nextWipeAt: timestamptz('next_wipe_at'),
    configSummary: jsonb('config_summary').$type<Record<string, unknown>>().notNull().default({}),
    lastSeenAt: timestamptz('last_seen_at'),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
  },
  (t) => [
    index('ix_game_servers_browse').on(t.listing, t.region),
    check('ck_game_servers_kind', sql`${t.kind} IN ('official','community')`),
    check('ck_game_servers_listing', sql`${t.listing} IN ('unlisted','listed','verified','delisted')`),
    check('ck_game_servers_mode', sql`${t.mode} IN ('pvp','pve')`),
    check('ck_game_servers_max_players', sql`${t.maxPlayers} BETWEEN 1 AND 200`),
  ],
);

/** Secrets game servers use to open the server link. Stored as SHA-256; shown once on creation. */
export const serverTokens = pgTable(
  'server_tokens',
  {
    id: uuid('id').primaryKey(),
    serverId: uuid('server_id')
      .notNull()
      .references(() => gameServers.id, { onDelete: 'cascade' }),
    tokenHash: bytea('token_hash').notNull(),
    createdAt: createdAt(),
    revokedAt: timestamptz('revoked_at'),
  },
  (t) => [unique('uq_server_tokens_hash').on(t.tokenHash), index('ix_server_tokens_server').on(t.serverId)],
);

export const playSessions = pgTable(
  'play_sessions',
  {
    id: uuid('id').primaryKey(),
    accountId: uuid('account_id')
      .notNull()
      .references(() => accounts.id, { onDelete: 'cascade' }),
    serverId: uuid('server_id')
      .notNull()
      .references(() => gameServers.id, { onDelete: 'cascade' }),
    joinedAt: timestamptz('joined_at').notNull(),
    leftAt: timestamptz('left_at'),
    leaveReason: text('leave_reason'),
  },
  (t) => [
    index('ix_play_sessions_account').on(t.accountId, t.joinedAt),
    index('ix_play_sessions_server').on(t.serverId, t.joinedAt),
  ],
);
