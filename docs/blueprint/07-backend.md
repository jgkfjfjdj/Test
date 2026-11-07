# 07 — Backend Platform

## 1. Shape: a modular monolith

A single TypeScript codebase with clear module boundaries, deployed as **two process types**:

| Process | Contents | Scaling |
|---------|----------|---------|
| `api` | Fastify HTTP (REST) + WebSocket endpoints (client realtime, server link, admin live) | Stateless; N replicas |
| `worker` | BullMQ job processors + repeatable scheduled jobs | N replicas (jobs are idempotent) |

Why not microservices: one developer, one deployment, shared transactions and much simpler operations.
Module boundaries (each with its own routes, service, repository and events) let us split a module
into its own service later if load requires it.

### Module layout

```
apps/api/src/modules/<module>/
  routes.ts        Fastify routes + TypeBox schemas (→ validation + OpenAPI)
  service.ts       business logic (no HTTP types)
  repository.ts    Drizzle queries
  events.ts        domain events emitted/consumed
  permissions.ts   (admin modules) required permissions per route
  *.test.ts        unit + integration tests (Testcontainers)
```

| Module | Responsibilities |
|--------|------------------|
| `auth` | Registration, login, Sign in with Apple, refresh rotation, email verification, password reset, App Attest, logout, token revocation |
| `accounts` | Account lifecycle, display names (unique, filtered), deletion and data export |
| `profiles` | Public profile, cosmetic loadout, cloud saves (settings, HUD layouts) |
| `social` | Friends, requests, blocks, presence, parties, invites |
| `servers` | Registry, owner management, server tokens, browser queries, favorites, history, reachability probes |
| `link` | Server link WebSocket hub, command routing, heartbeat ingestion |
| `tickets` | Join tickets (Ed25519 JWT), JWKS publishing, slot reservations |
| `matchmaking` | Quick Play, party placement |
| `moderation` | Reports, sanctions (global and server-scoped), chat filter lists, anti-cheat flag review |
| `liveops` | Remote config and feature flags, maintenance windows, announcements, live events |
| `seasons` | Seasons, challenges, progress, reward claiming |
| `versions` | Client version policy, server builds, content releases |
| `stats` | Stat ingestion from servers, aggregation, leaderboards (official scope) |
| `entitlements` | Cosmetic ownership, Apple purchase validation, App Store Server Notifications |
| `notifications` | APNs device tokens, preferences, push jobs, email jobs |
| `analytics` | Event ingestion, daily rollups, dashboard queries |
| `admin` | Staff auth (password + TOTP), RBAC, audit log, step-up auth, DB/Redis monitoring endpoints |
| `health` | Liveness/readiness, build info |

## 2. API conventions

- Base paths: `/v1/*` (players), `/server/v1/*` (game servers), `/admin/v1/*` (staff and owner
  portal), `/webhooks/*` (Apple).
- JSON only; `camelCase` fields; ISO-8601 UTC timestamps; UUIDv7 IDs.
- **Errors:** RFC 9457 `application/problem+json`, with a stable `code` (e.g. `auth.invalid_credentials`)
  that clients map to localized messages.
- **Pagination:** cursor-based (`?cursor=…&limit=…`), maximum 100 items per page.
- **Idempotency:** the `Idempotency-Key` header on POSTs that create something with side effects
  (purchases, reports, admin grants). Results are stored for 24 h in Redis.
- **Rate limits:** per IP and per account (Redis token buckets). `429` with `Retry-After`.
- **Versioning:** URL major version. Additive changes don't need a new version. Breaking changes go
  to `/v2` with a period where both run.
- OpenAPI 3.1 is generated at build time → `docs/api/openapi.json` → C# and TypeScript clients are
  generated from it.

## 3. REST API catalogue (v1)

### 3.1 Bootstrap and versions
| Method | Path | Description |
|--------|------|-------------|
| GET | `/v1/bootstrap` | Version status, maintenance, announcements, remote config, season, content catalog URL, server time |
| GET | `/v1/config` | Remote config snapshot (with ETag) |

### 3.2 Auth and account
| Method | Path | Description |
|--------|------|-------------|
| POST | `/v1/auth/register` | Email + password + display name → sends a verification email |
| POST | `/v1/auth/login` | Email + password → tokens |
| POST | `/v1/auth/apple` | Apple identity token (+ nonce) → tokens (account created or linked) |
| POST | `/v1/auth/refresh` | Rotate the refresh token (reuse detection revokes the whole family) |
| POST | `/v1/auth/logout` | Revoke the current refresh family |
| POST | `/v1/auth/verify-email` | Verify with the emailed token |
| POST | `/v1/auth/password/forgot` · `/reset` | Password reset flow |
| POST | `/v1/auth/attest/challenge` · `/attest` | App Attest key registration and assertions |
| GET/PATCH | `/v1/me` | Own account and profile basics |
| DELETE | `/v1/me` | **Account deletion request** (Apple requirement); confirms, then schedules the purge |
| POST | `/v1/me/export` | Request a data export (emailed link) |
| GET/PUT | `/v1/me/cloud-saves/{slot}` | `settings`, `hud`, `loadout`, `tutorial`. Version check prevents lost updates |

### 3.3 Players and social
| Method | Path | Description |
|--------|------|-------------|
| GET | `/v1/players/{id}` · `/v1/players/{id}/stats` | Public profile and stats |
| GET | `/v1/players/search?name=` | Search by display name |
| GET | `/v1/friends` | Friends + presence |
| POST | `/v1/friends/requests` · `/{id}/accept` · `/{id}/decline` | Requests |
| DELETE | `/v1/friends/{accountId}` | Remove a friend |
| PUT/DELETE | `/v1/blocks/{accountId}` | Block or unblock |
| POST | `/v1/parties` · `/{id}/invites` · `/{id}/join` · `/{id}/leave` | Parties (≤ 4) |

### 3.4 Servers, joining and matchmaking
| Method | Path | Description |
|--------|------|-------------|
| GET | `/v1/servers` | Browser: filters `region, official, mode, tags, notFull, hasPlayers, noPassword, wipedWithin, search`; sorting; cursor |
| GET | `/v1/servers/{id}` | Details (rules, wipe schedule, staff, live stats) |
| POST | `/v1/servers/{id}/join` | → join ticket + endpoint (body: optional password, party ID) |
| GET | `/v1/me/servers/recent` · PUT/DELETE `/v1/me/servers/favorites/{id}` | History and favorites |
| POST | `/v1/matchmaking/quick-play` | → chosen server + ticket(s) (see §6) |

### 3.5 Moderation, notifications, seasons, store, analytics
| Method | Path | Description |
|--------|------|-------------|
| POST | `/v1/reports` | Report a player (category, text, server, optional evidence request) |
| POST | `/v1/push/tokens` · GET/PUT `/v1/me/notification-prefs` | APNs registration and preferences |
| GET | `/v1/seasons/current` · `/v1/me/challenges` · POST `/v1/me/challenges/{id}/claim` | Seasons and challenges |
| GET | `/v1/me/entitlements` · POST `/v1/store/apple/transactions` | Cosmetics; verify StoreKit 2 signed transactions |
| POST | `/webhooks/apple/app-store` | App Store Server Notifications V2 (refunds, revocations) |
| POST | `/v1/analytics/events` | Batched client events (≤ 100 per batch) |

### 3.6 Server-to-backend
| Method | Path | Description |
|--------|------|-------------|
| WS | `/server/v1/link` | Server link (see [06 §5](06-dedicated-servers.md#5-server-link-protocol-game-server--backend)) |
| GET | `/server/v1/jwks` | Public keys for join tickets |
| GET | `/server/v1/builds/latest?channel=` | Signed server build manifest |
| GET | `/server/v1/config-schema` | JSON Schema for `server-config.json` |

### 3.7 Owner portal and staff admin
Listed page by page, with permissions, in [09](09-admin-panel.md). All under `/admin/v1/*`, using
cookie sessions. Owner-portal routes are scoped to servers the caller owns or is staff on.

## 4. WebSocket channels

| Endpoint | Auth | Events (server → client) | Client → server |
|----------|------|--------------------------|-----------------|
| `/v1/realtime` | Access token (first message) | `presence.update`, `friend.request`, `friend.accepted`, `party.invite`, `party.update`, `notice.maintenance`, `notice.announcement`, `config.changed`, `sanction.applied`, `match.found` | `presence.set` (menu/idle), `ping` |
| `/server/v1/link` | Server token | See [06 §5](06-dedicated-servers.md#5-server-link-protocol-game-server--backend) | |
| `/admin/v1/live` | Staff or owner session cookie | `servers.heartbeat` (subscribed set), `reports.new`, `metrics.tick`, `log.chunk`, `audit.new` | `subscribe`, `unsubscribe` |

Fan-out: each API instance subscribes to the Redis channels for its connected sockets
(`user:{id}`, `srvcmd:{serverId}`, `admin:live`). Any instance can publish.

## 5. Authentication and security design

| Topic | Design |
|-------|--------|
| Passwords | Argon2id (memory-hard parameters tuned to ~50 ms on API hardware). Rejects passwords found in breach lists (k-anonymity range check) |
| Access token | EdDSA (Ed25519) JWT, 15 min, claims `sub, sid (session family), scope, ver`. Key ID rotation through JWKS |
| Refresh token | Opaque, 256-bit, **rotated on every use**, stored as SHA-256, 60-day sliding expiry. Reusing an old token revokes the whole family (theft detection) |
| Sign in with Apple | Verify the identity token signature (Apple JWKS), `aud`, `iss`, nonce. Handle private-relay emails. Server-to-server revocation notifications |
| App Attest | Optional at first (a risk signal), can be required later via remote config. DeviceCheck bits mark banned devices |
| Rate limiting | Login: 5/min per account + 20/min per IP; register: 3/hour per IP; tickets: 10/min per account |
| Lockout | Progressive delay after failed logins; email alert on new-device login (optional) |
| Join tickets | Ed25519 JWT: `aud=serverId, sub=accountId, dn=displayName, cos=loadout, exp=60s, jti`. Signed with a separate key pair from access tokens |
| Staff auth | Separate `staff_users` table; password + **required TOTP** (WebAuthn/passkeys later); 8 h sessions; step-up re-auth for dangerous actions |
| Transport | HTTPS only (HSTS); TLS 1.2+; CORS limited to the console origin |
| Input validation | Every route has a TypeBox schema; unknown fields rejected; strings length-limited |
| Secrets | Environment variables from the secret store; never in the repo; signing keys rotated (overlapping JWKS) |
| Account deletion | Started in the app → 7-day cancellable grace → purge job: PII removed, gameplay references anonymized, a hashed identifier kept only if needed to enforce an active ban |

## 6. Matchmaking (Quick Play)

Survival games are mostly about choosing a server, so "matchmaking" here means **finding the best
server to join**, not building matches.

1. Candidates: online, protocol-compatible, listed, not full, no password, matching the player's mode
   preference (PvP/PvE, official or community).
2. **Ping:** the client measures RTT to regional beacons (and the top candidate servers' query ports)
   and sends a `region → ms` map. Servers above 120 ms are dropped when better ones exist.
3. Score = `0.40·pingScore + 0.25·fillScore + 0.15·wipeFreshness + 0.15·friendsPresent + 0.05·verified`.
   `fillScore` prefers 40–85% full, so new players land on lively but not packed servers.
4. **Reservation:** one Redis slot reservation per party member (60 s expiry) prevents overfilling
   when many people pick the same server at once. Then tickets are issued.
5. **Parties:** the leader triggers Quick Play; the server needs `partySize` free slots; every member
   gets `match.found` with a ticket.
6. **Queues:** if a chosen server is full, the player can join its queue (Redis list). The game server
   pulls from it as slots open. Position updates go over realtime.

## 7. Moderation

- **Reports:** category (cheating, harassment, offensive name/sign, griefing outside the rules, other),
  free text, automatic context (server, time, recent chat excerpt from the server, the reporter's
  position). A queue in the admin console sorted by severity and number of reports.
- **Sanctions:** `warn`, `mute`, `ban` × scope `global` or `server:{id}` × duration (or permanent).
  Each has a reason, evidence links, created/revoked by, and an expiry. Global bans block tickets and
  login (with a message showing the reason and end date). Server bans are enforced by that server and
  mirrored to the backend.
- **Chat and text filtering:** term lists (blocked and masked) + normalization (leetspeak, spacing)
  managed in the console, pushed to game servers. Applies to display names, chat, signs and server
  names. Players can turn off profanity masking for themselves but never blocked terms.
- **Blocking:** a blocked player's chat and gestures are hidden, and they can't send friend requests
  or invites.
- **Anti-cheat flags:** from game servers → review queue → a human decides. No automatic global bans
  in 1.0.
- **Appeals:** by email at launch; an in-console appeal workflow later.

## 8. Player statistics and progression
- Game servers send `stats_batch` every 60 s. Ingestion is idempotent (batch ID) and writes into
  `player_stats` (counters per scope: `official`, `community`, `season:{id}`).
- Global leaderboards use the `official` scope only. Profiles show both scopes, labeled.
- Account XP comes from official playtime and challenges. Levels unlock cosmetic rewards only.

## 9. Cloud saves
Small JSON documents per account and slot (`settings`, `hud`, `loadout`, `tutorial`), each with a
`version`. Writes must send the version they're based on; a mismatch returns `409` with the server copy
(the client merges or takes the newer one). Maximum 64 KB per slot.

## 10. Push notifications
APNs over HTTP/2 with token auth (.p8 key). Categories: `raid_alert`, `friend_request`, `party_invite`,
`event_start`, `wipe_reminder`. Each category has its own setting. Rate limits: raid alerts at most
1 per claim per 10 min and 6 per hour per user. Sent by the worker through BullMQ. Invalid tokens are
removed when APNs reports them.

## 11. Logging, metrics and tracing
- **Logs:** pino JSON to stdout → collected by Promtail/Alloy → **Loki**. Every request has a
  `requestId`; `accountId`/`serverId` are added when known. **PII redaction** (emails, tokens, IPs
  hashed). Retention 30 days.
- **Metrics:** `prom-client` → Prometheus: request rate/latency/errors per route, WebSocket
  connections, server link counts, job queue depths, DB pool usage, ticket issuance, logins.
  Grafana dashboards and Alertmanager rules (5xx rate, p95 latency, queue backlog, DB connections,
  zero heartbeats from official servers) live in `infra/observability`.
- **Errors:** Sentry for the API and worker (with release tags).
- **Tracing:** OpenTelemetry instrumentation added when debugging needs it (M10).

## 12. Analytics
- **Event schema:** `{name, v, ts, sessionId, props}`. Names come from a central catalogue
  (`packages/contracts/analytics-events.ts`), e.g. `app_open`, `login`, `tutorial_step`,
  `server_join`, `session_end`, `death`, `craft`, `build_place`, `purchase`, `fps_sample`,
  `crash_recovered`.
- Client batches send every 60 s or 50 events. Game servers send aggregate gameplay events.
- Stored in `analytics_events` (monthly partitions, 13-month retention), rolled up nightly into
  `daily_metrics` (DAU/WAU/MAU, new users, D1/D7/D30 retention, session length, CCU peaks per server
  and region, crashes per session, FPS percentiles per device tier).
- **Privacy:** no IDFA and no cross-app tracking (so no ATT prompt); an opt-out setting; data
  deletion follows account deletion.

## 13. Remote config and feature flags
- Entries: `key`, `value` (JSON), optional **targeting** (platform, version range, rollout percentage
  by stable account hash, account allowlist, environment), `description`, `updatedBy`.
- Clients get a snapshot at bootstrap plus `config.changed` pushes. Game servers get server-scoped
  keys over the link. Every change is audited and versioned (rollback = restore an older version).
- Examples: `voice.enabled`, `appAttest.required`, `quickPlay.weights`, `store.enabled`,
  `season.winter.enabled`, `perf.lowTier.viewDistance`.

## 14. Maintenance
- `maintenance_windows`: `starts_at`, `ends_at`, scope (`full` | `login` | `matchmaking` | `store`),
  message, platforms.
- Bootstrap reports `upcoming` (a banner 24 h ahead) and `active` (blocking for its scope). Admin can
  start an emergency maintenance immediately (step-up required). Game servers receive a broadcast
  and, for full maintenance, a scheduled shutdown.

## 15. Deployment
- **Images:** multi-stage Dockerfiles (Node 24 slim), running as a non-root user, read-only root
  filesystem, health checks.
- **Early production:** one VM (4 vCPU / 8 GB) with Docker Compose: Caddy, api ×2, worker ×1,
  PostgreSQL (or a managed PostgreSQL), Redis, Promtail. A second small VM for
  Prometheus/Grafana/Loki.
- **Migrations:** `drizzle-kit` SQL migrations, forward-only, run as a one-off job before rollout.
  Backward-compatible changes are required (expand → migrate → contract).
- **Releases:** CI builds images tagged with the git SHA → deploy staging → smoke tests → manual
  promote to production → rolling restart (no downtime for REST; WebSocket clients reconnect on their
  own).
- **Backups:** PostgreSQL base backups + WAL archiving (pgBackRest or provider PITR) → object storage.
  **RPO ≤ 5 min, RTO ≤ 1 h.** Restore drills every quarter (runbook in `docs/runbooks`).
