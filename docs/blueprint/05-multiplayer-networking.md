# 05 — Multiplayer Networking Architecture

## 1. Authority model

- **The server decides everything that matters.** Clients send *inputs* (movement axes, look angles,
  buttons) and *intents* (interact with entity X, craft recipe Y, place piece Z at a pose, move an item
  from A to B).
- The client **predicts only its own movement** and the cosmetic effects of its own actions (muzzle
  flash, swing animation, placement ghost). Everything else is rendered from server state.
- Every intent is validated on the server: does the entity exist, is it in range (≤ 3 m for
  interaction), is there line of sight, is it allowed (claim/lock/team), is it off cooldown, are
  resources available. Invalid intents are dropped and counted toward an anti-cheat score.

## 2. Library, transport and channels

| Layer | Choice |
|-------|--------|
| Netcode | ~~FishNet~~ → LiteNetLib + our own netcode ([ADR 0007](../adr/0007-netcode-litenetlib-own-simulation.md), [protocol](../protocol/game-netcode.md)) |
| Transport | Tugboat (LiteNetLib, UDP), **IPv6 dual-stack** |
| Channels | *Reliable ordered*: intents, inventory, chunk snapshots/deltas, chat, RPC results. *Unreliable*: player inputs (sent redundantly), remote entity state |
| MTU | Conservative payload of about 1 200 bytes, to stay under fragmentation thresholds on mobile carriers |
| Timeouts | Disconnect timeout 15 s (mobile-tolerant). Keep-alive 1 s |

## 3. Tick model

| Parameter | Value |
|-----------|-------|
| Server simulation tick | **30 Hz** (33.3 ms); configurable 20–60 |
| Client input send | Every tick. Each packet repeats the last 3 inputs (covers single packet loss) |
| Remote player state to near observers | 30 Hz (≤ 100 m), 10 Hz (100–250 m) |
| Animals and NPCs | 15 Hz near, 5 Hz far |
| World time and weather | 1 Hz + on change |
| Survival stats (owner only) | On change, at most 4 Hz |
| Chunk deltas | Batched per tick, sent when non-empty |

## 4. Prediction, reconciliation and interpolation

- **Local player:** tick-based prediction with a deterministic-enough character controller (kinematic
  capsule, our own movement code, not PhysX character movement). The server sends authoritative state
  for the last processed input tick. If the error is above 5 cm, the client rewinds to that tick and
  replays unacknowledged inputs; small errors are smoothed over ~100 ms.
- **Remote entities:** snapshot interpolation with an adaptive buffer (starts at 100 ms and grows with
  jitter, up to 200 ms). A short extrapolation (≤ 100 ms) covers gaps, then the entity freezes.
- **Actions with latency hiding:** weapon fire plays instantly on the client; hit markers appear only
  when the server confirms. Gathering plays the swing animation at once; the yield pop-up waits for
  the server.

## 5. Replication strategy by entity class

The most important scaling decision: **not everything is a FishNet NetworkObject.** A 50-player server
can easily hold 100 000+ persistent entities (building pieces, deployables, resource nodes). Making
each one a NetworkObject would overload server CPU, memory and observer rebuilds.

| Entity class | Typical count | Replication method |
|--------------|---------------|--------------------|
| Players | ≤ 100 | NetworkObject + prediction (owner) / interpolation (others) |
| Animals, NPCs | 50–200 active | NetworkObject (pooled); simulated only near players |
| Projectiles | Short-lived | Server-simulated; spawn events replicate; clients simulate visuals from the spawn data |
| Physics props (grabbed or thrown) | ≤ 200 active | NetworkObject while active; turned into a chunk entity when asleep |
| **Building pieces, deployables, containers, doors, dropped items at rest** | 10⁴–10⁵ | **Chunk replication** (below) |
| **Resource nodes** (trees, rocks, ores) | 10⁴–10⁵ (placed from map data) | **Chunk bitset**: only the depleted/alive state + respawn per node index |
| Loot spawner state | 10³ | Chunk replication |
| Inventories | Per player/container | **Owner/viewer-only deltas** over the reliable channel |

### 5.1 Chunk replication (custom, in `Kindling.Net`)

- The world is split into **64 m × 64 m chunks** (a 2 km map = 32 × 32 = 1 024 chunks).
- Each chunk holds a **versioned entity table**: `entityId → {typeId, pose (quantized), health tier,
  state flags, small payload}`.
- Clients **subscribe** to chunks inside their interest radius (with hysteresis to prevent
  subscribe/unsubscribe flapping).
  - On subscribe: the server sends a **chunk snapshot** (compressed, split across ticks if large).
  - After that: **deltas** (create/update/destroy) carrying the chunk version. If the client detects a
    gap, it asks for a resync.
- Pose quantization: position to 1 cm within the chunk (3 × 16-bit for x/z, 16-bit for y), rotation as
  a 32-bit "smallest three" (building pieces mostly use a 1-byte yaw index).
- Building pieces of one building are also grouped, so the client can rebuild combined meshes per
  building/chunk efficiently.
- Interacting with a chunk entity (open a door, loot a chest) goes through an intent RPC that carries
  the entity ID. The server checks it against its authoritative table.

## 6. Lag compensation and hit validation

- The server keeps a **hitbox history**: a ring buffer of 1 s of player hitbox poses (head, torso,
  limbs as capsules) recorded every tick.
- Every attack intent carries the client's **view tick** (the tick it was rendering, taking the
  interpolation delay into account). The server rewinds hitboxes to that tick, **capped at 250 ms**,
  then:
  - Melee and near-hitscan weapons: raycast or short sweep against the rewound hitboxes.
  - Projectiles: spawned at the rewound time and fast-forwarded (catch-up) so long-range bullets and
    arrows behave the same for everyone.
- World geometry (walls, terrain) is *not* rewound. Buildings change rarely, and a shot fired through
  a wall that has since been built is rejected.
- Players with ping above 250 ms get a warning (and suggested regions). The rewind cap means
  high-latency players have to lead their shots more. That's an intentional fairness trade-off.

## 7. Interest management

| Content | Radius | Notes |
|---------|--------|-------|
| Other players | 250 m (full rate ≤ 100 m) | Players inside closed structures, behind terrain, beyond 100 m: lower update rate. Server-side occlusion culling of players (anti-ESP) is a post-launch improvement |
| Building chunks | 400 m | Far chunks may use a "silhouette" representation (structure only, no deployables) |
| Deployables / containers / dropped items | 64–100 m | Reduces data and ESP exposure |
| Hidden caches / stashes | 5 m / 2 m | Never sent to clients farther away |
| Animals | 150 m | |
| Resource node bitsets | 300 m | Very cheap |
| Sounds (gunshots, explosions) | Event radius per sound type (up to 800 m for explosions) | Sent as one-shot events |

The observer grid is maintained incrementally as players cross chunk borders. Observer sets are not
rebuilt from scratch every tick.

## 8. Bandwidth budgets (per client)

| Direction | Average | Peak | Notes |
|-----------|---------|------|-------|
| Down | **15–25 KB/s** | ~80 KB/s while streaming chunks on join or when running into a large base | Peaks are spread across ticks |
| Up | 3–6 KB/s | 10 KB/s | Inputs + intents |
| Data per hour of play | ~60–90 MB | | Shown in the Settings data-usage info |

Server upload for 50 players ≈ 1.25 MB/s on average (~10 Mbps), 40 Mbps peaks. Hosting requirements
are listed in [06](06-dedicated-servers.md).

Techniques: delta compression against the last acknowledged state, quantization, bit-packing,
per-observer rate limiting, priority ordering (near/visible/threatening first) when the per-tick budget
is exceeded.

## 9. Player limits and how we validate them

**Why 50 at launch:** a Unity headless server runs most gameplay on one main thread. With 30 Hz ticks
(33 ms budget) we target **≤ 12 ms average and ≤ 25 ms at p99** for simulation + networking at full
population, to leave headroom for raids (explosions, stability recalculation) and saves. Phone clients
also limit how many detailed characters they can render at once. Fifty players on a 2 km map gives
the right density for encounters.

**Validation plan:**
1. **M1:** 2 iPhones + 30 headless bot clients on a Linux server. Measure tick time, bandwidth and
   prediction error.
2. **M6:** 50 bots running scripted behaviors (wander, gather, build, fight) on the full map; 24-hour
   soak test; memory growth < 5%/day.
3. **M10:** 75–100 bots + 10 real devices on staging hardware. Record the maximum population where p99
   tick time stays ≤ 25 ms. That number becomes the official cap, and the recommended community limit
   per hardware class.

**Bots:** headless Unity client builds (`-batchmode -nographics`) running a `BotBrain` that sends real
inputs through the real protocol. Each bot process uses ~150–250 MB of RAM, so one 32 GB machine runs
about 100 bots. Orchestrated by `tools/loadtest`.

## 10. Connection lifecycle

```
Connect(UDP) → Handshake{protocolVersion, joinTicket | resumeToken}
  → server verifies (signature, aud, exp, jti, bans, capacity, protocol)
  → Accepted{worldHeader, resumeToken} | Rejected{reasonCode}
→ Loading (client loads map, subscribes to chunks, server streams snapshots)
→ Spawned (in-game)
→ Disconnect: graceful (logout) | timeout | background
     → character stays as a sleeper (rules per server), resume token valid 120 s
```

Rejection reasons are numeric codes with localized messages: `version_mismatch`, `server_full`,
`banned`, `ticket_invalid`, `ticket_expired`, `maintenance`, `whitelist_only`.

## 11. Mobile network realities

| Problem | Mitigation |
|---------|-----------|
| Wi-Fi ↔ cellular switch changes the IP and drops the UDP session | Fast reconnect with the resume token, no new ticket. The character never leaves the world |
| App moved to the background (iOS suspends it within seconds) | `going_background` message; 120 s resume grace; sleeper rules |
| Jitter and bursty loss on cellular | Adaptive interpolation buffer; redundant inputs; reliable channel only where needed |
| **IPv6-only networks (NAT64)**, required by App Review | Dual-stack sockets. Clients resolve hostnames with the system resolver, so iOS synthesizes IPv6 for IPv4-only servers. Community servers that register an IPv4 literal still work through resolver synthesis. **Tested on a NAT64 network** (macOS Internet Sharing NAT64 mode) before every App Store submission |
| Carrier-grade NAT on the *client* | Fine: clients connect out to servers. Servers need a public IP and an open UDP port |

## 12. Security and anti-cheat (server side)

| Threat | Countermeasure |
|--------|----------------|
| Speed or teleport hacks | Server movement validation: maximum speed per state (walk/sprint/fall), corrected back with tolerance; repeat offenses → kick + flag |
| Noclip through walls | Server-side capsule sweep against static and building colliders |
| Interaction through walls | Server line-of-sight raycast for loot, doors and building |
| Fire-rate / no-reload hacks | Server-side weapon state machine (cooldowns, magazine, reload time) |
| Aimbots | Statistical flags (headshot ratio, snap angles, reaction times) → **flagged for review**, never automatic bans in 1.0 |
| ESP / wallhacks | Interest management limits; hidden-entity radii; post-launch player occlusion culling |
| Packet floods | Per-connection message rate limits; oversized messages dropped; connection-level kicks |
| Ticket replay | Single-use `jti`, 60 s expiry, audience bound to the server ID |
| Modified client | App Attest at login (integrity signal); the server never trusts client results |
| UDP amplification via the query port | Query replies are never larger than the request until a challenge is completed |

**Transport encryption:** v1 gameplay UDP is **not encrypted**. It carries no credentials (tickets
are short-lived, single-use and bound to one server; account tokens never go over UDP). Chat goes over
UDP in plain text, like most game protocols. We'll evaluate DTLS (Unity Transport) or an AEAD layer
with a session key derived from the ticket post-launch, once its CPU and battery cost can be measured.

## 13. Protocol versioning
- A single integer `PROTOCOL_VERSION` lives in `Kindling.Net`. It's bumped whenever wire formats or
  replicated content IDs change.
- Clients and servers must match exactly. The server browser hides incompatible servers (or shows them
  greyed out as "update required"). Version policy is described in
  [10](10-ios-distribution-and-liveops.md).
