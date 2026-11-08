# ADR 0009 — Persistent world: save format, stores, server-sent map, sleeping characters

- **Status:** Accepted (2026-11-08, M3). Adapts blueprint 06 §4 and 08 §4.

## Decisions

### 1. Save format: rows with versioned blobs
Characters and entities are rows with a few queryable columns (ids, type, chunk, position, owner,
health) plus a **versioned binary blob** for everything else (inventory with condition, crafting
queue, knowledge, stats, processor fuel/progress, cooldowns). Items are stored by their permanent
content id (ADR 0008); unknown ids are dropped with a warning instead of failing the load.
The blueprint's relational `containers`/`items` tables were dropped for now: blobs keep a character or
container atomic and simple. Resource nodes store only nodes that aren't full and alive.

### 2. Two stores behind one interface (`IWorldStore`)
- **`FileWorldStore`** (pure C#): the whole world in one file, replaced atomically (temp + rename),
  previous version kept as `.bak`, checksum with fallback to the backup. Used for offline play and
  Wi-Fi hosting on the phone and by the Unity dedicated server.
- **`SqliteWorldStore`** (.NET only, `sim/Kindling.Server`): blueprint schema adapted to (1), WAL +
  `synchronous=NORMAL`, one writer thread, one transaction per batch, online backups with rotation.
  Used by the .NET dedicated/dev server.
Both pass the same contract tests (exact round trip of a busy world, incremental saves, removals).
Saves: incremental batches every 60 s, full checkpoint every 30 min, character flush on disconnect,
full save on shutdown. Save timestamps use **world time**, which pauses while the server is down.

### 3. The server sends the static map
Map geometry and resource-node placements are sent to clients at join (~25 KB) with exact floats,
instead of each side regenerating them from the seed. Generation uses float math with rejection tests;
on iPhone (IL2CPP, ARM64) the C++ compiler may fuse multiply-adds, which could flip one test and shift
every later placement — the client's collision (prediction) would then disagree with the server's.
Sending the map removes that risk; real maps (M6) will be loaded from files anyway.

### 4. Characters persist and sleep
A character belongs to an account id (dev servers: the client's device id) and is never removed when
its player disconnects: it becomes a **sleeper** entity in the world (blueprint 01 §6.4). This
replaces M1's "resume token + 120 s grace" for keeping a player; reconnecting with the same account
takes the character back from anywhere (an older connection is closed with `Replaced`). Logging out
while downed counts as death. Sleeper modes: `vulnerable`, `protectedInClaim` (no claims until M4, so it
protects nobody yet) and `vanish` (hidden after 60 s).

## Consequences
- A save can be inspected with any SQLite tool; blobs need `SaveCodec` (tests cover every field).
- Admin queries like "who owns item X" need blob decoding until relational item tables are added.
- The Unity dedicated server can't use SQLite without a native plugin. Since the whole server core is
  engine-independent, **running the .NET server as the official dedicated server** (smaller, no Unity
  license needed for servers, SQLite built in) is a strong option to decide in M6–M7.
