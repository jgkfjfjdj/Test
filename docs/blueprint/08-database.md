# 08 — Database Design

## 1. Conventions (PostgreSQL)

- IDs: **UUIDv7** (time-ordered, generated in the app) unless noted.
- Timestamps: `timestamptz`, UTC. Every table has `created_at`; mutable tables also have `updated_at`.
- Naming: `snake_case`, plural table names, `fk_*` / `ix_*` / `uq_*` constraint names.
- Enumerations: `text` + `CHECK` constraints (easier to change than PG enums).
- Flexible payloads: `jsonb`, validated by the app's schemas.
- Emails: `citext`. Extensions: `citext`, `pg_stat_statements`, `pg_trgm` (name search).
- Migrations: Drizzle schema in `packages/db/src/schema/*` → SQL migrations in
  `packages/db/migrations`. Forward-only; expand/contract for breaking changes.
- The app connects as a role **without** `UPDATE`/`DELETE` on `audit_log` (append-only by grant).

## 2. Schema (core DDL)

The schema below defines the tables, keys and important constraints. Small details (exact lengths,
extra indexes) are finished in the milestone that implements each module.

### 2.1 Identity and access

```sql
CREATE TABLE accounts (
  id               uuid PRIMARY KEY,
  display_name     text NOT NULL,
  display_name_key text NOT NULL,                 -- normalized lowercase for uniqueness
  email            citext,                        -- nullable (Apple private relay or not provided)
  email_verified_at timestamptz,
  password_hash    text,                          -- null for Apple-only accounts
  status           text NOT NULL DEFAULT 'active'
                   CHECK (status IN ('active','suspended','pending_deletion','deleted')),
  deletion_requested_at timestamptz,
  last_login_at    timestamptz,
  created_at       timestamptz NOT NULL DEFAULT now(),
  updated_at       timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT uq_accounts_display_name_key UNIQUE (display_name_key),
  CONSTRAINT uq_accounts_email UNIQUE (email)
);

CREATE TABLE auth_identities (                    -- external logins
  id          uuid PRIMARY KEY,
  account_id  uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  provider    text NOT NULL CHECK (provider IN ('apple','game_center')),
  subject     text NOT NULL,                      -- provider's stable user id
  created_at  timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT uq_identity UNIQUE (provider, subject)
);

CREATE TABLE refresh_tokens (
  id           uuid PRIMARY KEY,
  account_id   uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  family_id    uuid NOT NULL,                     -- rotation family (reuse => revoke family)
  token_hash   bytea NOT NULL UNIQUE,             -- sha256
  device_id    uuid,
  expires_at   timestamptz NOT NULL,
  rotated_at   timestamptz,
  revoked_at   timestamptz,
  created_ip_hash bytea,
  created_at   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_refresh_tokens_account ON refresh_tokens(account_id) WHERE revoked_at IS NULL;

CREATE TABLE devices (
  id               uuid PRIMARY KEY,
  account_id       uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  platform         text NOT NULL CHECK (platform IN ('ios')),
  model            text, os_version text, app_version text,
  app_attest_key_id text,
  apns_token       text,
  apns_env         text CHECK (apns_env IN ('sandbox','production')),
  last_seen_at     timestamptz,
  created_at       timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE email_tokens (                        -- verification + password reset
  id uuid PRIMARY KEY, account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  purpose text NOT NULL CHECK (purpose IN ('verify','reset')),
  token_hash bytea NOT NULL UNIQUE, expires_at timestamptz NOT NULL, used_at timestamptz
);
```

### 2.2 Profiles, cloud saves, stats, progression

```sql
CREATE TABLE profiles (
  account_id   uuid PRIMARY KEY REFERENCES accounts(id) ON DELETE CASCADE,
  level        int  NOT NULL DEFAULT 1,
  xp           bigint NOT NULL DEFAULT 0,
  loadout      jsonb NOT NULL DEFAULT '{}',       -- equipped cosmetic ids
  privacy      jsonb NOT NULL DEFAULT '{}',       -- friend requests / joinability
  updated_at   timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE cloud_saves (
  account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  slot       text NOT NULL CHECK (slot IN ('settings','hud','loadout','tutorial')),
  version    int  NOT NULL,
  data       jsonb NOT NULL,                      -- app enforces <= 64 KB
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (account_id, slot)
);

CREATE TABLE player_stats (
  account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  scope      text NOT NULL,                       -- 'official' | 'community' | 'season:<id>'
  stat_key   text NOT NULL,                       -- 'kills_pvp','deaths','wood_gathered','playtime_s',...
  value      bigint NOT NULL DEFAULT 0,
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (account_id, scope, stat_key)
);
CREATE INDEX ix_player_stats_leaderboard ON player_stats(scope, stat_key, value DESC);

CREATE TABLE stat_batches (                        -- idempotent ingestion
  batch_id uuid PRIMARY KEY, server_id uuid NOT NULL, received_at timestamptz NOT NULL DEFAULT now()
);
```

### 2.3 Social

```sql
CREATE TABLE friend_requests (
  id uuid PRIMARY KEY,
  from_account uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  to_account   uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','accepted','declined','cancelled')),
  created_at timestamptz NOT NULL DEFAULT now(), responded_at timestamptz,
  CONSTRAINT uq_pending_request UNIQUE (from_account, to_account)
);

CREATE TABLE friendships (                          -- stored once, ordered pair
  account_low  uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  account_high uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  created_at   timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (account_low, account_high),
  CHECK (account_low < account_high)
);

CREATE TABLE blocks (
  blocker_id uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  blocked_id uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (blocker_id, blocked_id)
);
```

### 2.4 Servers

```sql
CREATE TABLE game_servers (
  id               uuid PRIMARY KEY,
  kind             text NOT NULL CHECK (kind IN ('official','community')),
  owner_account_id uuid REFERENCES accounts(id),   -- null for official
  name             text NOT NULL,
  description      text NOT NULL DEFAULT '',
  region           text NOT NULL,
  host             text,                            -- public hostname or IP reported/configured
  game_port        int, query_port int,
  max_players      int  NOT NULL DEFAULT 50 CHECK (max_players BETWEEN 1 AND 200),
  password_hash    text,                            -- argon2id from server hello; null = public
  map_id           text, seed int,
  mode             text NOT NULL DEFAULT 'pvp' CHECK (mode IN ('pvp','pve')),
  tags             text[] NOT NULL DEFAULT '{}',
  listing          text NOT NULL DEFAULT 'unlisted'
                   CHECK (listing IN ('unlisted','listed','verified','delisted')),
  delist_reason    text,
  build_version    text, protocol_version int,
  wipe_schedule    text, last_wipe_at timestamptz, next_wipe_at timestamptz,
  config_summary   jsonb NOT NULL DEFAULT '{}',     -- rates, sleepers, raid rules (for browser)
  last_seen_at     timestamptz,
  created_at       timestamptz NOT NULL DEFAULT now(),
  updated_at       timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_game_servers_browse ON game_servers(listing, region, protocol_version);
CREATE INDEX ix_game_servers_name_trgm ON game_servers USING gin (name gin_trgm_ops);

CREATE TABLE server_tokens (
  id uuid PRIMARY KEY,
  server_id  uuid NOT NULL REFERENCES game_servers(id) ON DELETE CASCADE,
  token_hash bytea NOT NULL UNIQUE,
  created_at timestamptz NOT NULL DEFAULT now(), revoked_at timestamptz
);

CREATE TABLE server_staff (
  server_id  uuid NOT NULL REFERENCES game_servers(id) ON DELETE CASCADE,
  account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  role       text NOT NULL CHECK (role IN ('admin','moderator')),
  added_by   uuid REFERENCES accounts(id),
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (server_id, account_id)
);

CREATE TABLE server_wipes (
  id uuid PRIMARY KEY, server_id uuid NOT NULL REFERENCES game_servers(id) ON DELETE CASCADE,
  kind text NOT NULL CHECK (kind IN ('map','full')), wiped_at timestamptz NOT NULL
);

CREATE TABLE server_metrics_1m (                   -- heartbeat aggregates for portal charts (30 days)
  server_id uuid NOT NULL REFERENCES game_servers(id) ON DELETE CASCADE,
  bucket    timestamptz NOT NULL,
  players_avg real, players_max int, tick_ms_avg real, tick_ms_p99 real, mem_mb int, entities int,
  PRIMARY KEY (server_id, bucket)
);

CREATE TABLE favorites (
  account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  server_id  uuid NOT NULL REFERENCES game_servers(id) ON DELETE CASCADE,
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (account_id, server_id)
);

CREATE TABLE play_sessions (
  id uuid PRIMARY KEY,
  account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  server_id  uuid NOT NULL REFERENCES game_servers(id) ON DELETE CASCADE,
  joined_at  timestamptz NOT NULL, left_at timestamptz,
  leave_reason text, client_version text
);
CREATE INDEX ix_play_sessions_account ON play_sessions(account_id, joined_at DESC);
CREATE INDEX ix_play_sessions_server  ON play_sessions(server_id, joined_at DESC);
```

### 2.5 Moderation

```sql
CREATE TABLE sanctions (
  id uuid PRIMARY KEY,
  account_id uuid NOT NULL REFERENCES accounts(id),
  kind   text NOT NULL CHECK (kind IN ('warn','mute','ban')),
  scope  text NOT NULL,                             -- 'global' | 'server:<uuid>'
  reason text NOT NULL,
  evidence jsonb NOT NULL DEFAULT '[]',
  starts_at timestamptz NOT NULL DEFAULT now(),
  expires_at timestamptz,                           -- null = permanent
  created_by_staff uuid, created_by_account uuid,   -- staff or server owner/staff
  revoked_at timestamptz, revoked_by uuid, revoke_reason text,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_sanctions_active ON sanctions(account_id, scope) WHERE revoked_at IS NULL;

CREATE TABLE reports (
  id uuid PRIMARY KEY,
  reporter_id uuid NOT NULL REFERENCES accounts(id),
  target_id   uuid NOT NULL REFERENCES accounts(id),
  server_id   uuid REFERENCES game_servers(id),
  category    text NOT NULL CHECK (category IN ('cheating','harassment','offensive_name','offensive_text','griefing','other')),
  message     text NOT NULL DEFAULT '',
  context     jsonb NOT NULL DEFAULT '{}',          -- chat excerpt, positions, client version
  status      text NOT NULL DEFAULT 'open' CHECK (status IN ('open','in_review','actioned','dismissed')),
  assigned_to uuid, resolution text, resolved_at timestamptz,
  created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_reports_queue ON reports(status, created_at);

CREATE TABLE anticheat_flags (
  id uuid PRIMARY KEY, account_id uuid NOT NULL REFERENCES accounts(id),
  server_id uuid REFERENCES game_servers(id), kind text NOT NULL, score real NOT NULL,
  details jsonb NOT NULL, status text NOT NULL DEFAULT 'open', created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE text_filter_terms (
  id uuid PRIMARY KEY, term text NOT NULL, action text NOT NULL CHECK (action IN ('block','mask')),
  locale text NOT NULL DEFAULT 'en', created_by uuid, created_at timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT uq_filter_term UNIQUE (term, locale)
);
```

### 2.6 Live ops, versions and content

```sql
CREATE TABLE client_versions (
  id uuid PRIMARY KEY,
  platform text NOT NULL DEFAULT 'ios',
  version  text NOT NULL,                          -- marketing version 1.4.0
  build    int  NOT NULL,                          -- CFBundleVersion
  protocol_version int NOT NULL,
  status   text NOT NULL CHECK (status IN ('testing','live','deprecated','blocked')),
  release_notes text, released_at timestamptz,
  CONSTRAINT uq_client_version UNIQUE (platform, build)
);
CREATE TABLE version_policy (                       -- single row per platform
  platform text PRIMARY KEY,
  min_supported_build int NOT NULL,                -- below => update_required
  latest_build int NOT NULL,                       -- below => update_available
  store_url text NOT NULL, updated_by uuid, updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE server_builds (
  id uuid PRIMARY KEY, version text NOT NULL UNIQUE, protocol_version int NOT NULL,
  channel text NOT NULL CHECK (channel IN ('stable','beta','staging')),
  artifacts jsonb NOT NULL,                        -- [{os, url, sha256, signature}]
  mandatory boolean NOT NULL DEFAULT false, release_notes text,
  status text NOT NULL CHECK (status IN ('draft','published','withdrawn')),
  published_by uuid, published_at timestamptz
);

CREATE TABLE content_releases (
  id uuid PRIMARY KEY, content_version text NOT NULL UNIQUE,
  min_build int NOT NULL, max_build int,           -- binary compatibility range
  catalog_url text NOT NULL, catalog_hash text NOT NULL,
  rollout_percent int NOT NULL DEFAULT 0 CHECK (rollout_percent BETWEEN 0 AND 100),
  status text NOT NULL CHECK (status IN ('draft','published','rolled_back')),
  published_by uuid, published_at timestamptz
);

CREATE TABLE remote_config (
  key text PRIMARY KEY, value jsonb NOT NULL, targeting jsonb NOT NULL DEFAULT '{}',
  scope text NOT NULL DEFAULT 'client' CHECK (scope IN ('client','server','both')),
  description text, version int NOT NULL DEFAULT 1, updated_by uuid, updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE remote_config_history (
  id uuid PRIMARY KEY, key text NOT NULL, version int NOT NULL, value jsonb NOT NULL,
  targeting jsonb NOT NULL, changed_by uuid, changed_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE maintenance_windows (
  id uuid PRIMARY KEY, scope text NOT NULL CHECK (scope IN ('full','login','matchmaking','store')),
  starts_at timestamptz NOT NULL, ends_at timestamptz, message jsonb NOT NULL,  -- localized
  created_by uuid, cancelled_at timestamptz
);

CREATE TABLE announcements (
  id uuid PRIMARY KEY, title jsonb NOT NULL, body jsonb NOT NULL,   -- localized
  channels text[] NOT NULL,                        -- {'menu','in_game','servers','push'}
  audience jsonb NOT NULL DEFAULT '{}',            -- targeting like remote config
  priority int NOT NULL DEFAULT 0, starts_at timestamptz NOT NULL, ends_at timestamptz,
  status text NOT NULL CHECK (status IN ('draft','scheduled','live','ended','cancelled')),
  created_by uuid, created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE seasons (
  id uuid PRIMARY KEY, key text NOT NULL UNIQUE,   -- 'winter_2028'
  name jsonb NOT NULL, starts_at timestamptz NOT NULL, ends_at timestamptz NOT NULL,
  content_pack text NOT NULL,                      -- season pack id in game content
  min_build int NOT NULL, config jsonb NOT NULL DEFAULT '{}',
  status text NOT NULL CHECK (status IN ('draft','scheduled','live','ended'))
);

CREATE TABLE live_events (
  id uuid PRIMARY KEY, season_id uuid REFERENCES seasons(id),
  kind text NOT NULL,                              -- 'multiplier','world_event','challenge_event'
  name jsonb NOT NULL, config jsonb NOT NULL,      -- e.g. {"gatherMultiplier":2}
  target jsonb NOT NULL DEFAULT '{"servers":"official"}',
  starts_at timestamptz NOT NULL, ends_at timestamptz NOT NULL,
  status text NOT NULL CHECK (status IN ('draft','scheduled','live','ended','cancelled')),
  created_by uuid
);

CREATE TABLE challenges (
  id uuid PRIMARY KEY, season_id uuid REFERENCES seasons(id), key text NOT NULL UNIQUE,
  name jsonb NOT NULL, description jsonb NOT NULL,
  objective jsonb NOT NULL,                        -- {"stat":"snowmen_built","target":5}
  reward jsonb NOT NULL,                           -- {"entitlements":["cos.hat.knit_red"],"xp":500}
  starts_at timestamptz NOT NULL, ends_at timestamptz NOT NULL
);
CREATE TABLE challenge_progress (
  account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  challenge_id uuid NOT NULL REFERENCES challenges(id) ON DELETE CASCADE,
  progress bigint NOT NULL DEFAULT 0, completed_at timestamptz, claimed_at timestamptz,
  PRIMARY KEY (account_id, challenge_id)
);
```

### 2.7 Economy (cosmetics only)

```sql
CREATE TABLE catalog_items (                        -- synced from content-export.json
  item_key text PRIMARY KEY, net_id int NOT NULL UNIQUE, category text NOT NULL,
  is_cosmetic boolean NOT NULL, season_key text, display jsonb NOT NULL
);
CREATE TABLE entitlements (
  id uuid PRIMARY KEY, account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  item_key text NOT NULL REFERENCES catalog_items(item_key),
  source text NOT NULL CHECK (source IN ('purchase','challenge','grant','level','event')),
  source_ref text, granted_at timestamptz NOT NULL DEFAULT now(), revoked_at timestamptz,
  CONSTRAINT uq_entitlement UNIQUE (account_id, item_key)
);
CREATE TABLE purchases (
  id uuid PRIMARY KEY, account_id uuid NOT NULL REFERENCES accounts(id),
  transaction_id text NOT NULL UNIQUE, original_transaction_id text NOT NULL,
  product_id text NOT NULL, environment text NOT NULL CHECK (environment IN ('sandbox','production')),
  status text NOT NULL CHECK (status IN ('verified','refunded','revoked')),
  signed_payload text NOT NULL, created_at timestamptz NOT NULL DEFAULT now()
);
```

### 2.8 Staff, RBAC and audit

```sql
CREATE TABLE staff_users (
  id uuid PRIMARY KEY, email citext NOT NULL UNIQUE, name text NOT NULL,
  password_hash text NOT NULL, totp_secret_enc bytea,      -- encrypted with app KMS key
  status text NOT NULL DEFAULT 'invited' CHECK (status IN ('invited','active','disabled')),
  last_login_at timestamptz, created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE staff_roles (id text PRIMARY KEY, name text NOT NULL, permissions text[] NOT NULL);
CREATE TABLE staff_user_roles (
  staff_id uuid NOT NULL REFERENCES staff_users(id) ON DELETE CASCADE,
  role_id  text NOT NULL REFERENCES staff_roles(id),
  PRIMARY KEY (staff_id, role_id)
);
CREATE TABLE staff_sessions (
  id uuid PRIMARY KEY, staff_id uuid NOT NULL REFERENCES staff_users(id) ON DELETE CASCADE,
  token_hash bytea NOT NULL UNIQUE, step_up_at timestamptz, expires_at timestamptz NOT NULL,
  ip_hash bytea, user_agent text, revoked_at timestamptz
);

CREATE TABLE audit_log (                            -- append-only; partitioned monthly
  id uuid NOT NULL, occurred_at timestamptz NOT NULL DEFAULT now(),
  actor_type text NOT NULL CHECK (actor_type IN ('staff','owner','server','system')),
  actor_id uuid, action text NOT NULL,             -- 'sanction.create','inventory.op','config.update',...
  target_type text, target_id text,
  reason text, before jsonb, after jsonb, result text,
  request_id text, ip_hash bytea,
  PRIMARY KEY (id, occurred_at)
) PARTITION BY RANGE (occurred_at);
CREATE INDEX ix_audit_target ON audit_log(target_type, target_id, occurred_at DESC);
CREATE INDEX ix_audit_actor  ON audit_log(actor_type, actor_id, occurred_at DESC);
```

### 2.9 Analytics

```sql
CREATE TABLE analytics_events (
  id uuid NOT NULL, received_at timestamptz NOT NULL DEFAULT now(),
  client_ts timestamptz, account_id uuid, session_id uuid,
  name text NOT NULL, v smallint NOT NULL, props jsonb NOT NULL DEFAULT '{}',
  app_version text, device_tier text,
  PRIMARY KEY (id, received_at)
) PARTITION BY RANGE (received_at);

CREATE TABLE daily_metrics (
  day date NOT NULL, metric text NOT NULL, dimension text NOT NULL DEFAULT '', value double precision NOT NULL,
  PRIMARY KEY (day, metric, dimension)
);
```

## 3. Redis keyspace

| Key | Type | TTL | Purpose |
|-----|------|-----|---------|
| `presence:{accountId}` | hash `{status, serverId, since}` | 90 s (refreshed) | Friends' presence |
| `srv:live:{serverId}` | hash (players, max, queue, tickAvg, tickP99, fps, memMb, version, protocol, wipeAt) | 30 s (refreshed by heartbeat) | Server browser, portal |
| `srv:index:{region}` | sorted set (score = ranking) | rebuilt continuously | Fast browser queries |
| `srv:reserve:{serverId}` | sorted set (member = accountId, score = expiry) | members expire after 60 s | Slot reservations |
| `srv:queue:{serverId}` | list | until consumed | Join queue |
| `ticket:jti:{jti}` | string | 120 s | Single-use enforcement (backend side) |
| `party:{partyId}` | hash + `party:{id}:members` set | 24 h idle | Parties |
| `rl:{bucket}:{key}` | string/counter | window length | Rate limiting |
| `idem:{key}` | string (stored response) | 24 h | Idempotency |
| `revoked:access:{sid}` | string | 15 min | Early revocation of access tokens |
| `cfg:version` | string | — | Remote config cache invalidation |
| Pub/sub `user:{accountId}`, `srvcmd:{serverId}`, `admin:live` | channels | — | WebSocket fan-out |
| `bull:*` | BullMQ | — | Jobs |

Everything in Redis can be rebuilt (from heartbeats within 10 s, or from PostgreSQL), so losing Redis
means a short disruption, not lost data.

## 4. World save database (SQLite, per game server)

> **Update (M3):** implemented with an adapted schema (versioned blobs instead of relational
> containers/items, `resource_nodes` instead of per-chunk bitsets) — see
> [ADR 0009](../adr/0009-persistent-world-model.md).

```sql
CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
-- schema_version, world_id, map_id, seed, created_at, wipe_id, protocol_version, last_save_tick

CREATE TABLE entities (
  id          INTEGER PRIMARY KEY,                  -- server-unique 64-bit id
  type_id     INTEGER NOT NULL,                     -- content net id (piece, deployable, container, prop)
  chunk_x     INTEGER NOT NULL, chunk_z INTEGER NOT NULL,
  px REAL NOT NULL, py REAL NOT NULL, pz REAL NOT NULL,
  rot         INTEGER NOT NULL,                     -- packed rotation
  owner_id    BLOB,                                 -- account uuid (placer)
  building_id INTEGER,                              -- group for building pieces
  health      REAL NOT NULL,
  tier        INTEGER,                              -- building tier
  flags       INTEGER NOT NULL DEFAULT 0,           -- open/locked/etc.
  data        BLOB,                                 -- type-specific compact payload (versioned)
  updated_at  INTEGER NOT NULL
);
CREATE INDEX ix_entities_chunk ON entities(chunk_x, chunk_z);
CREATE INDEX ix_entities_building ON entities(building_id);

CREATE TABLE buildings (id INTEGER PRIMARY KEY, claim_entity_id INTEGER, created_at INTEGER NOT NULL,
                        last_upkeep_at INTEGER, decay_state REAL NOT NULL DEFAULT 0);
CREATE TABLE claim_auth (claim_entity_id INTEGER NOT NULL, account_id BLOB NOT NULL, added_at INTEGER NOT NULL,
                         PRIMARY KEY (claim_entity_id, account_id));
CREATE TABLE locks (entity_id INTEGER PRIMARY KEY, kind INTEGER NOT NULL, code_hash BLOB, key_id INTEGER,
                    authorized BLOB);                -- packed account list

CREATE TABLE containers (id INTEGER PRIMARY KEY, owner_kind INTEGER NOT NULL, -- entity|character
                         owner_ref BLOB NOT NULL, role INTEGER NOT NULL,      -- main|hotbar|wear|storage
                         slots INTEGER NOT NULL);
CREATE TABLE items (id INTEGER PRIMARY KEY, container_id INTEGER NOT NULL, slot INTEGER NOT NULL,
                    item_net_id INTEGER NOT NULL, amount INTEGER NOT NULL, condition REAL, data BLOB,
                    UNIQUE (container_id, slot));

CREATE TABLE characters (
  account_id BLOB PRIMARY KEY, display_name TEXT NOT NULL,
  alive INTEGER NOT NULL, px REAL, py REAL, pz REAL, yaw REAL,
  stats BLOB NOT NULL,                              -- health/hunger/thirst/warmth/effects
  knowledge BLOB NOT NULL,                          -- learned recipe bitset
  team_id INTEGER, sleeping INTEGER NOT NULL, last_seen INTEGER NOT NULL,
  respawn_points BLOB                               -- owned bed entity ids
);
CREATE TABLE teams (id INTEGER PRIMARY KEY, leader BLOB NOT NULL, members BLOB NOT NULL);
CREATE TABLE resource_chunks (chunk_x INTEGER, chunk_z INTEGER, alive_bits BLOB NOT NULL,
                              respawn_queue BLOB, PRIMARY KEY (chunk_x, chunk_z));
CREATE TABLE loot_spawners (id INTEGER PRIMARY KEY, next_refill INTEGER NOT NULL, state BLOB);
CREATE TABLE server_bans (account_id BLOB PRIMARY KEY, reason TEXT, expires_at INTEGER, by_account BLOB);
CREATE TABLE event_log (id INTEGER PRIMARY KEY, at INTEGER NOT NULL, kind TEXT NOT NULL,
                        actor BLOB, target TEXT, data TEXT);  -- raids, deaths, admin actions (14-day retention)
```

The `data` payloads are versioned binary formats owned by `Kindling.Simulation` serializers, with unit
tests for upgrades from older versions.

## 5. Data retention and privacy

| Data | Retention |
|------|-----------|
| Accounts | Until deletion. Purge 7 days after the request; ban-enforcement hashes kept while the ban is active |
| Refresh tokens | Removed 30 days after expiry or revocation |
| Play sessions | 13 months |
| Analytics raw events | 13 months (partition drop); rollups kept |
| Audit log | 2 years (security and compliance) |
| Reports + evidence | 1 year after resolution |
| Server event logs (world DB) | 14 days |
| Application logs (Loki) | 30 days |
| IP addresses | Stored only as keyed HMAC hashes (for abuse correlation) |

## 6. Backups and disaster recovery

| Store | Method | RPO | RTO |
|-------|--------|-----|-----|
| PostgreSQL | Base backups (daily) + continuous WAL archive to object storage (or managed PITR) | ≤ 5 min | ≤ 1 h |
| Redis | AOF every second; rebuildable | n/a | Minutes |
| World DBs (official) | Hourly online backup → object storage; per-wipe archive | ≤ 1 h (≤ 60 s locally) | ≤ 15 min per server |
| Object storage | Versioned buckets; content and builds can also be rebuilt from CI | — | — |

## 7. Database monitoring
- `pg_stat_statements` for top queries; slow query log (> 200 ms).
- Exporters: `postgres_exporter`, `redis_exporter` → Prometheus → Grafana.
- The admin console's read-only **Database** page shows connections, cache hit ratio, slow queries,
  table and index sizes, replication lag (if any) and Redis memory/ops (see [09](09-admin-panel.md)).
- PgBouncer (transaction pooling) is added once API replicas × pool size gets close to
  `max_connections`.
