# Architecture Decision Records

Short, dated records of decisions that are expensive to reverse. Format: context → decision →
consequences. Supersede a record by adding a new one; never edit an accepted decision's outcome.

| # | Decision | Status |
|---|----------|--------|
| [0001](0001-engine-unity-6-lts.md) | Unity 6 LTS as the game engine | Accepted 2026-11-07 |
| [0002](0002-netcode-fishnet.md) | FishNet for netcode, behind our own `Kindling.Net` layer | Accepted, validation gate in M1 |
| [0003](0003-backend-node-fastify.md) | Node.js + TypeScript + Fastify modular monolith | Accepted 2026-11-07 |
| [0004](0004-world-persistence-sqlite.md) | SQLite world database per game server | Accepted 2026-11-07 |
| [0005](0005-integration-tests-real-services.md) | Integration tests run against real PostgreSQL/Redis services | Accepted 2026-11-07 |
