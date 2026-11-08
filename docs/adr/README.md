# Architecture Decision Records

Short, dated records of decisions that are expensive to reverse. Format: context → decision →
consequences. Supersede a record by adding a new one; never edit an accepted decision's outcome.

| # | Decision | Status |
|---|----------|--------|
| [0001](0001-engine-unity-6-lts.md) | Unity 6 LTS as the game engine | Accepted 2026-11-07 |
| [0002](0002-netcode-fishnet.md) | FishNet for netcode, behind our own `Kindling.Net` layer | **Superseded by 0007** |
| [0003](0003-backend-node-fastify.md) | Node.js + TypeScript + Fastify modular monolith | Accepted 2026-11-07 |
| [0004](0004-world-persistence-sqlite.md) | SQLite world database per game server | Accepted 2026-11-07 |
| [0005](0005-integration-tests-real-services.md) | Integration tests run against real PostgreSQL/Redis services | Accepted 2026-11-07 |
| [0006](0006-m2-identity-and-trust-decisions.md) | Identity, session and trust decisions (M2) | Accepted 2026-11-07 |
| [0007](0007-netcode-litenetlib-own-simulation.md) | LiteNetLib transport + our own engine-independent simulation and netcode | Accepted 2026-11-08 |
| [0008](0008-content-as-json-data.md) | Game content as JSON data with permanent network ids | Accepted 2026-11-08 |
| [0009](0009-persistent-world-model.md) | Persistent world: save format, stores, server-sent map, sleeping characters | Accepted 2026-11-08 |
