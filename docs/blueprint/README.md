# KINDLING — Technical Blueprint v0.1

> **Status:** Draft for approval · **Date:** 2026-11-07 · **Code written so far:** none
>
> After you approve or amend this blueprint, implementation starts with **Milestone 0**
> (see [14-roadmap.md](14-roadmap.md)).

"KINDLING" is a codename. It is used in namespaces, assemblies and package names so that a later rename
of the public title doesn't touch code. Before any public use, the final title needs a trademark search.

---

## 1. Executive summary

We are building an **original, first-person, multiplayer survival game for iPhone**, with:

- **Immersive, hands-on interaction** (inspired by the physical feel of social VR survival games, rebuilt
  for touch): you pick up, carry, place and throw physical objects, use interactive stations, and talk to
  people nearby.
- **Deep open-world survival** (inspired by the gather → craft → build → raid loop of PC survival games):
  resources, crafting tiers, base building with upgrades, locks and permissions, raiding, PvP, looting
  and persistent servers that are wiped on a schedule.
- **A full ecosystem**: authoritative dedicated servers, a server browser and quick-play matchmaking,
  community-hosted servers with auto-updates, a backend (accounts, social, moderation, live ops,
  analytics), a web admin console and seasonal content updates.

The architecture rests on one rule: **the client is never trusted.** Game servers hold all authority over
the world, inventories and building. The backend holds all authority over accounts, entitlements and
platform-wide moderation. Community servers are trusted with *their own world only*.

---

## 2. Key decisions at a glance

| Area | Decision | Why (short) — details in |
|------|----------|--------------------------|
| Game engine | **Unity 6 LTS** (exact patch pinned in M0), URP, IL2CPP | Best-proven engine for 3D multiplayer on iOS; one project builds both the iOS client and the Linux dedicated server; C# iterates fast — [02](02-technology-stack.md) |
| Netcode | ~~FishNet~~ → **LiteNetLib + own engine-independent netcode** (ADR 0007, M1) | Free, self-hostable, has prediction built in, no per-player fees. Validated in M1 before we commit — [02](02-technology-stack.md), [05](05-multiplayer-networking.md) |
| Transport | UDP (LiteNetLib, used directly since ADR 0007), IPv6 dual-stack | Low latency; tolerates mobile packet loss; Apple requires IPv6-only (NAT64) support — [05](05-multiplayer-networking.md) |
| World replication | NetworkObjects for *dynamic* entities only; **custom chunk replication** for buildings, resource nodes, containers and deployables | Tens of thousands of static entities can't each be a networked object on a mobile-scale server — [05](05-multiplayer-networking.md) |
| World persistence | **SQLite world database per game server** (WAL), plus backups to object storage | Works the same on official and community servers; transactional; crash-safe — [06](06-dedicated-servers.md) |
| Backend | **Node.js 24 LTS + TypeScript, Fastify**, modular monolith (`api` + `worker` processes) | You asked for it; the workload is I/O-bound; the admin panel shares the TypeScript contracts — [07](07-backend.md) |
| Database | **PostgreSQL 17** (Drizzle ORM, SQL migrations) + **Redis 7.x** (or the Valkey drop-in) | Relational core data; Redis for presence, live server state, rate limits, queues and pub/sub — [08](08-database.md) |
| Realtime (meta) | WebSockets: client gateway (presence, invites, notices), **server link** (game server ↔ backend), admin live feed | Gameplay traffic never passes through the backend — [07](07-backend.md) |
| Admin console | **React + Vite + TypeScript**, TanStack Router/Query, Tailwind + shadcn/ui; responsive | Works on desktop, tablet and iPhone; one app with separate *Staff* and *Server Owner* areas — [09](09-admin-panel.md) |
| Infra | Docker everywhere; Docker Compose for dev and early production; Caddy (TLS); Prometheus + Grafana + Loki; Sentry | Simple to run and cheap. Kubernetes is deferred until there's a real need — [07](07-backend.md) |
| CI/CD | GitHub Actions; GameCI for Unity; Fastlane for TestFlight | I can read CI logs, which gives us a feedback loop for Unity compile errors — [14](14-roadmap.md) |
| Distribution | App Store + TestFlight; **Addressables** for downloadable *data/asset* content (never code); backend-driven version gate, maintenance and remote config | Complies with App Store rules — [10](10-ios-distribution-and-liveops.md) |
| Seasons | Data-driven **Season Packs** (definitions + Addressables + server overrides + challenges), turned on by date from the backend | Each new season is mostly content, not code — [11](11-seasonal-content-and-winter-update.md) |

---

## 3. Realistic targets (to be checked by measurement)

| Target | Value at launch | Notes |
|--------|-----------------|-------|
| Players per official server | **50** | Raised only after load tests pass. See [05 §9](05-multiplayer-networking.md#9-player-limits-and-how-we-validate-them) |
| Players per community server | 50 by default, **up to 100 (experimental)** | Depends on host hardware; the server warns when tick time degrades |
| Total population | Grows **horizontally** (more servers), not through one giant shard | The backend is stateless and scales out |
| Map size | **2 km × 2 km** launch map (engine supports up to 4 km × 4 km) | Fits 50 players; keeps mobile memory and streaming manageable |
| Server tick | 30 Hz | Configurable 20–60 |
| Client frame rate | 30 FPS on Low/Medium tiers, 60 FPS on the High tier | Thermal-aware dynamic scaling |
| Minimum device | **iPhone 11 / iPhone SE (2nd gen), A13** | Final floor set by M6 profiling. See [04 §7](04-client-and-ios-controls.md#7-graphics-tiers-and-performance-budgets) |
| Mobile data use | ~60–90 MB per hour of play (estimate) | Budget: ~15–25 KB/s down on average |
| Time to App Store launch | **~12–18 months** for one full-time developer working with me, plus bought or contracted art | See [14](14-roadmap.md). A small team shortens this a lot |

---

## 4. Read before approving

1. **"Winter 2025" has already passed.** Today is November 2026, and the game is starting from nothing.
   The Winter Update is fully designed in [11](11-seasonal-content-and-winter-update.md) and the season
   system is built in M9. A realistic schedule is **a reduced winter event during the open beta
   (Dec 2027 – Jan 2028)** and **the full Winter Update for the first winter after launch (Winter
   2028–29)**. I suggest calling it "Winter Update" and adding the year once the date is fixed.
2. **iOS builds need a Mac with Xcode.** Apple allows no other way. Cloud Macs (CI runners, Unity Build
   Automation) can build and upload to TestFlight, but you will need an iPhone to test on, and having a
   Mac for daily work is strongly recommended. See [13](13-software-and-accounts.md).
3. **I can't run the Unity Editor, Xcode or an iPhone from this environment.** I can write all the C# and
   editor tooling, and run and test the backend and admin panel here. For Unity we set up a loop: CI
   compiles the Unity project and runs tests (I read the logs), and you test on a device and send me
   logs, screenshots and profiler captures.
4. **Art, animation and audio are the biggest content cost.** I'll build every system with greybox and
   procedural placeholders and supply editor tools that generate them. Production-quality 3D models,
   animations and sound need Asset Store purchases (properly licensed), contracted artists, or both.
5. **Scope is huge.** The published PC/VR survival games this project draws on took large teams many
   years. The roadmap is ordered so that every milestone ends with something **playable on an iPhone**,
   and features nobody can play yet are never reported as done.

---

## 5. Decisions I need from you

| # | Question | My recommendation |
|---|----------|-------------------|
| 1 | Approve **Unity 6 LTS + FishNet** (with a validation gate in M1) | Yes |
| 2 | Approve the **Node/TypeScript + PostgreSQL + Redis** backend | Yes |
| 3 | Working title: keep "KINDLING" internally? | Yes. Pick the public title before the TestFlight beta |
| 4 | Monetization model | **Cosmetics only** (no pay-to-win), sold through Apple In-App Purchase. Can be added after launch |
| 5 | Team and budget: solo, or a few contractors (3D artist, animator, sound)? | At least one contract 3D artist from M6 onward |
| 6 | Hardware you have: Mac? Which iPhones? | One older iPhone (A13/A14) and one recent iPhone for testing |
| 7 | First server regions | Start with **one** region (where most players are), add more later |
| 8 | Proximity voice chat in 1.0? | Design for it, decide after M7 (moderation and App Review cost) |
| 9 | Default rules for official servers: offline-raid protection, sleepers | Partial offline protection on by default (mobile players can't defend 24/7) |

---

## 6. Document map

| # | Document | Contents |
|---|----------|----------|
| 01 | [Game design](01-game-design.md) | Pillars, core loop, world, survival, building, raiding, combat, interaction, social |
| 02 | [Technology stack](02-technology-stack.md) | Engine, netcode and backend evaluations and choices; versions; licensing |
| 03 | [System architecture](03-system-architecture.md) | Ecosystem diagram, trust boundaries, data ownership, main flows, environments |
| 04 | [Client and iPhone controls](04-client-and-ios-controls.md) | Unity architecture, touch controls, HUD editor, settings, graphics tiers, performance |
| 05 | [Multiplayer networking](05-multiplayer-networking.md) | Authority, tick, prediction, interest management, chunk replication, anti-cheat, player limits |
| 06 | [Dedicated servers](06-dedicated-servers.md) | Server runtime, persistence, server link, official fleet, community hosting, updates, monitoring |
| 07 | [Backend](07-backend.md) | Modules, REST and WebSocket APIs, auth, matchmaking, moderation, logging, analytics, deployment |
| 08 | [Database](08-database.md) | PostgreSQL schema, Redis keyspace, world-save schema, retention, backups |
| 09 | [Admin panel](09-admin-panel.md) | Staff console, RBAC, audit, every page mapped to its backend API; server owner portal |
| 10 | [iOS distribution and live ops](10-ios-distribution-and-liveops.md) | App Store, TestFlight, versioning, downloadable content, remote config, IAP, compliance |
| 11 | [Seasons and the Winter Update](11-seasonal-content-and-winter-update.md) | Season framework, full Winter Update plan |
| 12 | [Folder structure](12-folder-structure.md) | Monorepo layout |
| 13 | [Software and accounts](13-software-and-accounts.md) | What to install and sign up for, and costs |
| 14 | [Roadmap](14-roadmap.md) | Milestones M0–M12 with deliverables, exit criteria and estimates |
| 15 | [Risks and limitations](15-risks-and-limitations.md) | Risk register, technical challenges, what I can and can't do |
