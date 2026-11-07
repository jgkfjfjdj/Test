# ADR 0004 — SQLite world database per game server

- **Status:** Accepted (2026-11-07)
- **Details:** docs/blueprint/06-dedicated-servers.md §4, docs/blueprint/08-database.md §4

## Context
World state (buildings, deployables, inventories, characters) changes constantly and belongs to one
game server. Community servers run on owners' machines without our database.

## Decision
Each game server persists its world to a local **SQLite** file in WAL mode. Dirty entities are
serialized on the main thread every 60 s and written in one transaction by a background thread;
checkpoints every 30 min; hourly online backups (to object storage for official servers).
Platform data (accounts, entitlements, sanctions, stats) stays in PostgreSQL.

## Consequences
- Identical persistence for official and community servers; no network dependency for saves.
- At most ~60 s of world changes lost on a crash; never a half-written save.
- Admin tools reach world data through the server link, not by querying SQLite directly.
