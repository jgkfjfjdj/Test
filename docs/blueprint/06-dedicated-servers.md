# 06 — Dedicated Servers (Official and Community)

## 1. Server runtime

The **game server** is the Unity Dedicated Server build (Linux x86_64; a Windows x64 build is offered
for community convenience). Internally it runs:

| Part | Thread | Role |
|------|--------|------|
| Simulation loop | Main | 30 Hz tick: inputs, gameplay systems, networking |
| Jobs | Worker threads (Burst) | Stability recalculation, decay sweeps, AI sensing, chunk serialization |
| Persistence writer | Background thread | SQLite transactions from serialized dirty-entity buffers |
| Server link client | Background (async) | WSS connection to the backend: heartbeats, events, commands |
| Query responder | Background | UDP query port: ping + basic info for the server browser |
| Metrics endpoint | Background | `GET /metrics` (Prometheus format) bound to localhost or a private network |
| Log sink | Background | JSON lines to rotating files + stdout |

## 2. Configuration

Precedence: **CLI args → environment variables → `server-config.json` → defaults.** Owners can change
most settings from the Owner Portal; changes reach the server over the server link and are written
back to the file. Settings that need a restart are marked as such.

```jsonc
{
  "server": {
    "name": "Northshore | 2x Gather | Biweekly",
    "description": "Friendly admins, no toxicity.",
    "region": "eu-west",
    "maxPlayers": 50,
    "password": "",                      // empty = public. Only an argon2id hash is sent to the backend
    "bindAddress": "0.0.0.0",
    "port": 27015,                        // UDP game port
    "queryPort": 27016,                   // UDP query port
    "tags": ["pvp", "2x", "biweekly"],
    "motd": "Welcome! Read the rules at the spawn notice board."
  },
  "backend": {
    "url": "https://api.kindling.example",
    "serverToken": "${KINDLING_SERVER_TOKEN}"   // never committed; env var recommended
  },
  "world": {
    "mapId": "map.reach_2k",
    "seed": 8812,
    "dayLengthMinutes": 60,
    "nightFraction": 0.25,
    "weatherProfile": "temperate_default"
  },
  "gameplay": {
    "pvp": true,
    "gatherMultiplier": 2.0,
    "craftSpeedMultiplier": 1.0,
    "teamSizeLimit": 4,
    "aimAssistAllowed": true,
    "globalChat": true,
    "decay": { "enabled": true, "multiplier": 1.0 },
    "upkeep": { "enabled": true, "multiplier": 1.0 }
  },
  "raiding": {
    "offlineDamageMultiplier": 0.5,
    "offlineGraceMinutes": 15,
    "raidWindows": []                     // e.g. [{ "days": "mon-sun", "from": "18:00", "to": "23:00" }]
  },
  "sleepers": { "mode": "protected-in-claim" },   // vulnerable | protected-in-claim | vanish
  "wipe": {
    "schedule": "every-2-weeks-thursday-19:00",
    "timezone": "Europe/Berlin",
    "knowledgeWipe": false
  },
  "saves": { "intervalSeconds": 60, "checkpointMinutes": 30, "backupsToKeep": 48 },
  "updates": { "auto": true, "window": "05:00-06:00", "timezone": "Europe/Berlin" },
  "seasonal": { "followGlobalSeason": true },
  "access": { "whitelistOnly": false }
}
```

The config is validated at startup against a JSON Schema published by the backend
(`packages/contracts`). An invalid config stops the server with a clear message naming the bad field.

## 3. Startup sequence
1. Parse and validate the config → open the world DB (migrate the schema if needed) → load content and map.
2. Restore the world from the DB (entities into chunks, characters as sleepers).
3. Open the server link: `hello` → receive the JWKS for join tickets, live season/event config, the
   chat filter list and the global ban list delta.
4. Open the UDP game and query ports → report `ready` → appear in the server browser.

If the backend can't be reached at startup, the server **keeps retrying with backoff** and runs in
*offline* mode. Players who already hold valid tickets or resume tokens can still connect.
New joins need tickets, so they resume when the backend comes back.

## 4. World persistence (SQLite)

- One file per world (`world.db`), **WAL mode**, `synchronous=NORMAL`, single writer (the persistence
  thread).
- **Dirty tracking:** systems mark entities dirty. Every 60 s the main thread serializes the dirty set
  into byte buffers (a fast memcpy-style step, < 2 ms for typical batches), then hands them to the
  writer thread, which commits **one transaction**.
- **Checkpoint** (all entities) every 30 minutes; full save on graceful shutdown; immediate flush of a
  character on disconnect.
- **Crash recovery:** at most ~60 s of world changes can be lost. The SQLite transaction guarantees
  the save is never half-written.
- **Backups:** hourly online backups (SQLite backup API) → zstd → `backups/` (+ object storage for
  official servers). Retention follows `backupsToKeep`. One archive is kept for every wipe.
- **Wipes:** *map wipe* (new world, knowledge kept), *full wipe* (world + knowledge). Scheduled wipes
  announce themselves in-game at 24 h / 1 h / 10 min, and in the server browser ("Wiped 2 h ago").

The world schema is in [08 §4](08-database.md#4-world-save-database-sqlite-per-game-server).

## 5. Server link protocol (game server ↔ backend)

**Transport:** WSS to `wss://api…/server/v1/link`. Authenticated with `Authorization: Bearer <server
token>`. Server tokens are random 256-bit secrets, stored **hashed** in the backend, revocable and
rotatable from the Owner Portal.

**Envelope:** `{ "t": "type", "id": "uuid?", "re": "uuid?", "d": { … } }`. Requests carry `id`;
responses carry `re`. Every message has a schema in `packages/contracts/server-link`, and the
matching C# types are generated from it.

| Direction | Message | Purpose |
|-----------|---------|---------|
| S → B | `hello` | Build version, protocol, ports, config summary, password hash, map, wipe time |
| S → B | `heartbeat` (every 10 s) | Players online/max, queue, tick ms (avg/p99), FPS, memory, entity count, uptime |
| S → B | `player_joined` / `player_left` | Presence, play sessions, consuming slot reservations |
| S → B | `stats_batch` (every 60 s) | Kills, deaths, gathered, crafted, built, raids, playtime (labeled official or community) |
| S → B | `challenge_progress` (official only) | Seasonal challenge counters |
| S → B | `report_evidence` | Chat excerpt + context attached to a player report |
| S → B | `anticheat_flag` | Suspicious-behavior record for review |
| S → B | `raid_alert` | Claim under explosive attack → push notification job |
| S → B | `log_chunk` | Only while a log tail is open in the portal or console |
| B → S | `jwks_update`, `config_update`, `event_start` / `event_end`, `season_update`, `chat_filter_update`, `ban_delta` | Configuration pushes |
| B → S | `cmd.broadcast`, `cmd.kick`, `cmd.ban`, `cmd.unban`, `cmd.restart` (with countdown), `cmd.shutdown`, `cmd.save`, `cmd.wipe_schedule` | Owner/staff commands |
| B → S | `cmd.inventory.get`, `cmd.inventory.op` (add/remove/move, with a reason), `cmd.teleport`, `cmd.player.info` | Moderation (staff-only on official servers; owners on their own servers) |
| B → S | `log_tail.start` / `log_tail.stop` | Live log streaming |

Commands are **idempotent** (by command ID) and time out after 10 s. Each one is audited on the backend.

## 6. Official server fleet

- **Hosts:** bare metal or dedicated-core VMs in the target region(s), DDoS-protected for UDP.
- **Density:** one game server per **2 dedicated cores** (≥ 3.5 GHz) and **8 GB RAM**. A 16-core /
  64 GB host runs about 6–7 servers with headroom. To be confirmed in M10.
- **`kindling-host` in fleet mode:** reads a desired-state list from the backend (which servers, with
  which config and build), runs each server in a Docker container (`--cpuset-cpus` pinning,
  memory limits), restarts crashed servers, reports container health and pulls signed builds.
- **Deploying a new server build to the fleet:** staging first → canary (1 server) → the rest. Each
  server restarts in its update window, or immediately with a 10-minute warning when a client protocol
  bump requires it.

## 7. Community self-hosted servers

### 7.1 What owners get
- **Docker image** `ghcr.io/<org>/kindling-host:<channel>`: contains the supervisor. It downloads,
  verifies and runs the game server build in a mounted volume. Recommended path.
- **Standalone packages** for people who don't use Docker: Linux tarball and Windows zip, each
  with the supervisor as a single executable.
- A documentation site: requirements, port forwarding, config reference, and the rules for listing.

### 7.2 Hardware guidance (to be confirmed by M10 load tests)

| Players | CPU | RAM | Upload bandwidth | Disk |
|---------|-----|-----|------------------|------|
| ≤ 20 | 2 cores @ 3 GHz+ | 4 GB | 10 Mbps | 10 GB SSD |
| ≤ 50 | 2 dedicated cores @ 3.5 GHz+ | 8 GB | 50 Mbps | 20 GB SSD |
| ≤ 100 (experimental) | 4 dedicated cores @ 3.8 GHz+ | 16 GB | 100 Mbps | 40 GB SSD |

The server monitors its own tick time. If p99 stays above 30 ms for 5 minutes, it warns the owner
(portal banner + log) and the browser shows the server as "performance degraded".

### 7.3 Registration and listing
1. The owner signs in to the **Owner Portal** with their player account → *Create server* → picks name,
   region and tags → receives a **server token** (shown once).
2. They put the token into the config or env var and start the server.
3. The server sends `hello` → the backend's **reachability probe** (worker) sends a UDP query to the
   reported public host:port. If it succeeds, the server is listed. If it fails, the portal shows
   port-forwarding guidance.
4. Listing states: `unlisted` (private, join by ID or link), `listed`, `verified` (granted by staff:
   badge + better browser ranking), `delisted` (by staff, with a reason).
5. **Community server rules (shown on registration):** no selling of in-game advantages; no links to
   external purchases in names, descriptions or MOTD (this protects our App Store standing); must
   follow content rules; must not impersonate official servers. Breaking the rules → delisting, and
   for serious cases, owner account sanctions.

### 7.4 Automatic updates
- The backend publishes **server build manifests**: `{version, protocol, channel, url, sha256,
  ed25519Signature, mandatory, releaseNotes}`.
- The supervisor checks every 15 minutes (and immediately on a `build_available` push over the link),
  downloads to a staging folder, **verifies SHA-256 and the Ed25519 signature** against the public key
  embedded in the supervisor, and then:
  - **Mandatory** (protocol bump): in-game countdown 10/5/1 min → save → swap → restart.
  - **Optional:** applies in the owner's update window.
- **Rollback:** if the new build fails its health check (no `ready` within 3 minutes, or 3 crashes in
  10 minutes), the supervisor restores the previous build and reports `update_failed`.
- The supervisor updates itself through the Docker image tag (Docker path) or the same signed manifest
  flow (standalone path).

### 7.5 Restart controls
The portal or console sends `cmd.restart {delaySeconds, message}`. The game server broadcasts a
countdown, saves and exits with code 0. The supervisor restarts it. Owners can also set **scheduled
restarts** (e.g. daily at 05:00) in the config or portal.

### 7.6 Monitoring and logs
- **Live:** player count, tick time (avg/p99), FPS, memory, entities, uptime, version. Shown in the
  portal (from heartbeats in Redis) and in the server browser (players, queue).
- **History:** the backend stores 1-minute aggregates of heartbeats for 30 days (for portal charts).
  Official servers also expose Prometheus metrics to our monitoring stack.
- **Logs:** JSON lines, rotated daily, kept 14 days by default. The portal **Logs** tab streams a live
  tail over the link (`log_tail.start`) and lets owners download recent files through the supervisor.
  Official server logs are shipped to Loki.

### 7.7 Owner permissions on their server

| Capability | Owner | Server Admin | Server Moderator |
|------------|:-----:|:------------:|:----------------:|
| Edit config, wipe schedule, password | ✓ | ✓ | |
| Restart / shutdown / save | ✓ | ✓ | |
| Manage server staff | ✓ | | |
| Rotate server token, delete server | ✓ | | |
| Kick, mute | ✓ | ✓ | ✓ |
| Server ban / unban | ✓ | ✓ | ✓ (temporary only) |
| View and edit player inventories, teleport | ✓ | ✓ | |
| View logs | ✓ | ✓ | ✓ |
| Broadcast messages, run server events | ✓ | ✓ | |

Server staff can use these powers **only on that server**, both in the portal and through in-game admin
commands (`/kick`, `/ban`, `/tp`, `/broadcast`…). Every use is logged in the server's audit trail,
which the owner can see.

## 8. Security of the server ecosystem

- Server tokens: hashed at rest, revocable, scoped to one server, rate-limited link connections.
- Server build integrity: signed manifests and signatures checked by the supervisor.
- The backend treats every community-server report as untrusted input: schema-validated,
  rate-limited and kept out of official stats.
- The query port is protected against reflection/amplification (see [05 §12](05-multiplayer-networking.md#12-security-and-anti-cheat-server-side)).
- Players see a note in the browser: *"Community servers are run by independent hosts who can see
  your IP address."* The privacy policy says the same.
