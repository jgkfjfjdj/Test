# 02 — Technology Stack and Engine Selection

## 1. Requirements that drive the choice

1. A 3D first-person open world running at **30–60 FPS on iPhones from the A13 up**, with thermal headroom.
2. **Server-authoritative multiplayer** with client-side prediction, lag compensation and large numbers
   of persistent entities.
3. A **headless dedicated server for Linux** that community members can host for free (no per-player fees).
4. A small team (one developer plus me) → **fast iteration**, code that is text and can be reviewed and
   generated, and a large licensed asset ecosystem.
5. A path to **commercial release** with predictable licensing costs.

## 2. Engine evaluation

| Criterion | **Unity 6 LTS** | Unreal Engine 5 | Godot 4 |
|-----------|-----------------|-----------------|---------|
| iOS 3D performance and tooling | Mature: many shipped 3D multiplayer mobile titles; Metal; Unity Profiler + Memory Profiler + Frame Debugger + Xcode Instruments | Capable (high-end mobile titles have shipped), but a heavier baseline in CPU, GPU, memory and install size; mobile renderer is a reduced feature set; tight thermal headroom on older phones | Runs on iOS; far fewer shipped high-end 3D iOS games; the Apple-native rendering path is relatively new |
| Dedicated headless server | **Built-in Dedicated Server build target** (strips rendering and audio), Linux x86_64 | Excellent dedicated servers, but you must **build the engine from source** to get them | Headless export works; you build the server architecture yourself |
| Multiplayer ecosystem | Many solid options (FishNet, Mirror, Netcode for GameObjects/Entities, Photon) | Best built-in replication of the three | Basic high-level API; no built-in prediction or lag compensation |
| Language and iteration | C#; fast compile; easy for AI to write and review | C++ (slow builds) + Blueprints (binary assets: hard to diff, review or generate) | GDScript or C# (C# on iOS is still maturing) |
| Asset ecosystem | **Largest** (Asset Store): crucial for a small team's art budget | High quality (Fab), often too heavy for mobile | Small |
| Licensing | Personal: free under US$200k revenue/funding in the past 12 months; Pro subscription above that | Free, then 5% royalty on gross revenue above US$1M | MIT, free |
| Risk | Unity's business decisions (the runtime fee was cancelled in 2024; licensing terms can change) | Steep learning curve and mobile performance | Lack of proven examples at this scope |

### Decision: Unity 6 LTS

Unity is the most realistic engine for **an ambitious 3D multiplayer game on iPhone made by a small team**.
One project builds both the iOS client and the Linux dedicated server, so gameplay code and content
definitions are shared. The exact Unity 6 LTS patch version gets pinned in `ProjectSettings/ProjectVersion.txt`
in M0, and engine upgrades happen only on purpose, between milestones.

**Unity configuration:**

| Setting | Choice | Reason |
|---------|--------|--------|
| Render pipeline | **URP** (Universal Render Pipeline) with mobile-tuned assets per quality tier | The standard mobile path; SRP Batcher; Forward+ where it helps |
| Scripting backend | **IL2CPP** (required on iOS; also used for the Linux server for speed) | AOT; performance |
| Input | **Input System** package (EnhancedTouch, gamepads) | Multi-touch and controller support |
| Content | **Addressables** | Streaming, memory control, downloadable seasonal content |
| Hot paths | **Burst + Jobs** (stability, decay, AI sensing, chunk serialization) | Uses multiple cores without moving to full DOTS |
| UI | **uGUI + TextMeshPro** for HUD and menus | Proven touch performance; easy custom shaders. UI Toolkit can be revisited later |
| Localization | Unity Localization package | Built in |
| Tests | Unity Test Framework (EditMode/PlayMode) + a separate `dotnet test` project for pure-C# rules | Fast CI without needing a Unity license for most tests |

We **don't** use full ECS/DOTS for gameplay. It would make the architecture much more complex for little
gain at 50 players. We use plain C# simulation code with data-oriented stores for bulk entities, and
MonoBehaviours for presentation.

## 3. Netcode evaluation

| Option | Authority model | Prediction / reconciliation | Cost | Community self-hosting | Verdict |
|--------|-----------------|------------------------------|------|------------------------|---------|
| Unity Netcode for GameObjects | Server or host | No complete built-in client-side prediction | Free | Yes | Viable, but lots of DIY |
| Unity Netcode for Entities | Server, with prediction | Yes | Free | Yes | Strong, but **forces an ECS/DOTS architecture**: too much complexity for this team |
| **FishNet** | Server-authoritative | **Yes, built in** (tick-based prediction) | Free for commercial use (license re-checked at M1) | Yes | **Chosen** |
| Mirror | Server-authoritative | Community add-ons / DIY | MIT, free | Yes | **Fallback** |
| Photon Fusion 2 | Server / host / shared | Excellent | Per-CCU pricing; dedicated servers tied to their plans | Hard for community servers | Rejected (cost and hosting model) |
| Custom on LiteNetLib | Anything | DIY | Free | Yes | Too much to build first |

**Why FishNet:** tick-based prediction and reconciliation, observer (interest management) conditions,
broadcasts for custom message streams, compile-time code generation that is IL2CPP-safe, and a
LiteNetLib UDP transport (Tugboat). No CCU fees means community servers cost us nothing.

**Our own systems on top of FishNet** (so we don't depend on library internals): hitbox history and
lag compensation, chunk-based world replication, a join ticket handshake, resume tokens and the server
link. If the M1 gate fails (see [14](14-roadmap.md)), these systems move to Mirror with limited rework,
because gameplay code talks to our own `Kindling.Net` interfaces, not straight to FishNet everywhere.

## 4. Backend stack

| Concern | Choice | Notes |
|---------|--------|-------|
| Runtime | **Node.js 24 LTS** | Active LTS; matches the user's stack preference |
| Language | **TypeScript** (strict) | Shared types with the admin console |
| HTTP framework | **Fastify** | Fast, schema-first, good plugin system |
| Schemas and validation | **TypeBox** (JSON Schema) → runtime validation + **OpenAPI 3.1** generation | One source of truth; C# DTOs are generated from the OpenAPI spec |
| WebSockets | `@fastify/websocket` (`ws`) + Redis pub/sub for fan-out across instances | Client gateway, server link, admin live |
| ORM / SQL | **Drizzle ORM** + drizzle-kit migrations (SQL kept in the repo); `postgres` driver | Type-safe, close to SQL, no heavy runtime |
| Redis client | `ioredis` | |
| Jobs | **BullMQ** (Redis) | Emails, push, rollups, cleanup, scheduled tasks |
| Auth crypto | `argon2` (Argon2id passwords), `jose` (EdDSA JWTs, JWKS) | Ed25519 signing keys with rotation |
| Logging | `pino` (JSON) | → Loki |
| Metrics / tracing | `prom-client`; OpenTelemetry (later) | → Prometheus/Grafana |
| Errors | Sentry SDK | Also used in Unity and React |
| Testing | **Vitest**; **Testcontainers** (real PostgreSQL/Redis in integration tests) | |
| Apple integrations | App Store Server Library (Node); APNs over HTTP/2 with token auth; Sign in with Apple identity token verification (Apple JWKS) | |
| Email | A transactional provider (Postmark, Amazon SES or Resend) behind an interface | |

**Why not ASP.NET Core (C#) to share code with Unity?** It's a reasonable alternative. You listed
Node.js/TypeScript, the admin console is TypeScript, and the backend never runs gameplay simulation.
Contracts are shared through OpenAPI code generation, so the language split costs little.

## 5. Data stores

| Store | Use | Version |
|-------|-----|---------|
| **PostgreSQL** | Accounts, social graph, server registry, moderation, live ops, entitlements, stats, audit, analytics (at first) | 17 (pinned in Docker; upgrades are planned) |
| **Redis** | Presence, live server index, slot reservations, single-use ticket IDs, rate limits, BullMQ, pub/sub | 7.x (or **Valkey 8**, a BSD-licensed drop-in; we only use standard commands) |
| **SQLite** (embedded in each game server) | World save: entities, buildings, inventories, characters | Bundled with the server build |
| **S3-compatible object storage** | Server builds, world backups, Addressables content, database backups | Cloudflare R2 / AWS S3 / Backblaze B2 / MinIO (dev) |
| **ClickHouse** (later, only if needed) | High-volume analytics | Added when Postgres analytics becomes a bottleneck |

## 6. Web admin console

| Concern | Choice |
|---------|--------|
| Framework | **React 19 + Vite + TypeScript** (SPA) |
| Routing / data | TanStack Router + TanStack Query |
| UI | Tailwind CSS + shadcn/ui (Radix primitives): accessible and responsive |
| Charts | Apache ECharts (or Recharts) |
| Forms | React Hook Form + the same TypeBox/JSON schemas |
| API client | Generated from the backend's OpenAPI spec |
| Auth | httpOnly secure session cookies + CSRF token; required TOTP for staff (see [09](09-admin-panel.md)) |

## 7. Infrastructure and operations

| Concern | Choice |
|---------|--------|
| Containers | Docker images for `api`, `worker`, `web`, `game-server`, `host` |
| Orchestration (start) | **Docker Compose** on 1–2 Linux VMs for the backend; game servers on dedicated hosts run by our `kindling-host` fleet agent |
| Orchestration (later) | Kubernetes (+ Agones for game servers) only when fleet size justifies it |
| Reverse proxy / TLS | **Caddy** (automatic HTTPS) |
| DNS / CDN | Cloudflare (DNS, CDN for Addressables, basic DDoS protection for the HTTP API) |
| Game server hosting | Dedicated servers or VPSs with dedicated CPU cores and DDoS-protected UDP, from a provider with a region near the players |
| Observability | Prometheus + Grafana + Loki + Alertmanager; Sentry for crashes and exceptions |
| Secrets | Environment variables from a secret store (start: SOPS-encrypted files or provider secrets; later: Vault or a cloud secret manager) |
| CI/CD | GitHub Actions; **GameCI** (Unity tests and Linux server builds); **Fastlane** (iOS signing, TestFlight upload) on a macOS runner or your Mac |
| Container registry | GitHub Container Registry (GHCR) |

## 8. Third-party SDKs on the client (kept to a minimum)

| SDK | Purpose | Notes |
|-----|---------|-------|
| FishNet | Netcode | Vendored at a pinned version |
| Sentry Unity SDK | Crash and exception reporting | Privacy-reviewed; no ad tracking |
| Sign in with Apple plugin (open source, MIT) or a small native bridge | Apple login | |
| Unity IAP (StoreKit 2 based) or a thin native StoreKit 2 bridge | Cosmetics purchases (post-launch) | Validated on the server |
| Voice SDK (Vivox or a self-hosted Opus path) | Proximity voice | **Decision deferred** to after M7 |

No advertising SDKs and no cross-app tracking → no App Tracking Transparency prompt is needed.

## 9. Version pinning policy

- Unity: one LTS patch per milestone; upgrades get their own PR with a profiling comparison.
- Node: the active LTS line; minor updates monthly.
- npm dependencies: lockfile committed; Renovate (or Dependabot) PRs, grouped weekly.
- FishNet and other vendored C# libraries: pinned version in `ThirdParty/VERSIONS.md` with the license file.
