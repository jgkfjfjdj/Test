# 15 — Risks, Technical Challenges and Realistic Limitations

## 1. Honest framing

- Commercial survival games of this kind were built by large studios over many years, and they keep
  growing after launch. We can build a **real, shippable, original** game with this architecture, but
  **1.0 will be smaller in content** (one map, a focused item set, no vehicles or electricity) than
  mature PC titles. The systems are designed so that content can grow after launch.
- No feature will be reported as done unless it has been shown working against its exit criteria.

## 2. What I can and can't do in this environment

| I can | I can't |
|-------|---------|
| Write and review all C# (gameplay, networking, server, editor tools, iOS bridge glue) | Open the Unity Editor, run Play mode, or look at scenes visually |
| Write, run and test the backend, worker, web console and host tooling here | Build for iOS, sign apps, or run anything on an iPhone |
| Write Docker, CI and infrastructure configuration; run the backend stack here | Test on real cellular networks or real devices |
| Read CI logs (Unity compile errors, test failures) through GitHub | Make production 3D art, animation, audio or music |
| Generate placeholder/greybox content through editor scripts | Create accounts, buy services, submit to App Review |
| Write documentation, runbooks and legal *drafts* | Give legal advice (privacy policy and terms need review by a qualified person) |

**Consequence:** Unity work depends on a feedback loop with you plus CI. Setting up Unity CI in M0 is
important to keep that loop fast.

## 3. Risk register

| # | Risk | Likelihood | Impact | Mitigation |
|---|------|:---:|:---:|------------|
| R1 | **Scope creep / overall size** | High | High | Strict milestone exit criteria; "out of scope for 1.0" list ([01 §17](01-game-design.md#17-explicitly-out-of-scope-for-10-candidates-for-later)); cut content before cutting quality |
| R2 | **Art and audio production** (cost, consistency, mobile-ready assets) | High | High | Stylized art direction; licensed asset packs with consistent style; budget a contract artist from M6; greybox-first |
| R3 | **Mobile performance and thermals** on A13 devices | Medium | High | Budgets from M1; profiling every milestone; tiers + thermal scaling; benchmark route; raise the minimum device if needed (decision in M6) |
| R4 | **Memory limits** (iOS terminates apps that use too much memory) | Medium | High | Memory budgets; Addressables streaming; Increased Memory Limit entitlement; memory warning handling; soak tests |
| R5 | **Cellular network quality** (jitter, loss, IP changes) | High | Medium | Prediction + adaptive interpolation; redundant inputs; resume tokens; regional servers; tested on LTE/5G from M1 |
| R6 | **Unity server performance ceiling** (main-thread bound) | Medium | Medium | Chunk replication instead of NetworkObjects; Burst jobs; 50-player cap until load tests prove more; horizontal scaling |
| R7 | **Netcode library limits or bugs** | Medium | High | Our own abstractions (`Kindling.Net`); M1 decision gate; Mirror fallback; vendored, pinned version |
| R8 | **Cheating** (modified clients, aimbots, ESP) | Medium | High | Server authority; validation; interest management; App Attest; statistical flags + human review; report tools from the alpha |
| R9 | **Untrusted community servers** (modified builds, fake stats, abuse) | High | Medium | No ticket minting; stats separated by scope; rewards only from official servers; listing rules; delisting; privacy notice |
| R10 | **App Review rejections** (UGC, IPv6, purchases, account deletion, voice) | Medium | Medium | Compliance checklist ([10 §8](10-ios-distribution-and-liveops.md#8-app-store-compliance-checklist-checked-before-every-submission)) before every submission; TestFlight external review early (M7) to catch issues sooner |
| R11 | **Touch vs. controller fairness** in PvP | Medium | Medium | Longer time-to-kill; touch-only aim slowdown; no assist for controllers; server options; community labels |
| R12 | **Offline raiding feeling unfair** for mobile players | High | Medium | Offline protection settings, raid windows, sleeper modes, push raid alerts; official defaults tuned in the beta |
| R13 | **Moderation workload** (chat, names, signs, voice) | Medium | High | Filters, block, report tools, a moderator console that works on phones; voice deferred until moderation capacity is ready |
| R14 | **Server hosting costs** | Medium | Medium | Small fleet in 1 region; dedicated-core density; scale with CCU; community servers carry a share of the population |
| R15 | **Data loss** (world or platform) | Low | High | SQLite transactions + backups; PostgreSQL PITR; restore drills; signed builds |
| R16 | **Security breach** (accounts, admin) | Low | High | Argon2id, rotating refresh tokens, staff TOTP + step-up, RBAC, audit log, dependency scanning, secrets management, security review in M10 |
| R17 | **Licensing changes** (Unity, libraries, Redis) | Low | Medium | Pinned versions; Valkey fallback for Redis; Mirror fallback; record of licenses in `docs/licenses` |
| R18 | **Legal/IP** (similarity to existing games, the final title) | Low | High | Originality rules in [01 §3](01-game-design.md#3-inspiration--original-implementation); no copied names or assets; trademark search before public naming |
| R19 | **Single-developer bus factor** | Medium | High | Everything in the repo: ADRs, runbooks, tests, CI; no knowledge only in someone's head |
| R20 | **Proximity voice chat** (bandwidth, battery, moderation, App Review) | Medium | Medium | Off by default, push-to-talk, feature-flagged; go/no-go after M7 |

## 4. Key technical challenges (and our approach)

1. **Many persistent entities on a mobile-scale server.** Custom chunk replication + data-oriented
   stores + combined building meshes, instead of one networked object per piece ([05 §5](05-multiplayer-networking.md#5-replication-strategy-by-entity-class)).
2. **Fair hit registration with mobile latency.** Server-side lag compensation with a 250 ms rewind
   cap; projectiles caught up to the shot time; server-confirmed hit markers ([05 §6](05-multiplayer-networking.md#6-lag-compensation-and-hit-validation)).
3. **Interruptions are normal on iOS.** A 120 s resume token, sleeper modes and one-tap Resume make
   phone calls and app switches survivable ([03 §5.6](03-system-architecture.md#56-interruptions-mobile)).
4. **Precise building on a touchscreen.** Ghost preview with snapping, explicit place/rotate buttons,
   reasons shown when placement fails ([04 §3.4](04-client-and-ios-controls.md#34-build-mode-on-touch)).
5. **Open-world rendering on A13.** Tiers, impostors, combined meshes, strict draw-call and memory
   budgets, thermal scaling ([04 §7](04-client-and-ios-controls.md#7-graphics-tiers-and-performance-budgets)).
6. **Content updates without code downloads.** Season systems ship in the binary early; Addressables
   carry only data and assets ([10 §5](10-ios-distribution-and-liveops.md#5-downloadable-content-dlc-on-ios)).
7. **Trust between platform and community servers.** Signed tickets, scoped tokens, separated stats,
   signed builds ([03 §3](03-system-architecture.md#3-trust-boundaries)).
8. **Persistence without stalling the tick.** Dirty tracking, serialize on the main thread, write on a
   background thread, SQLite WAL ([06 §4](06-dedicated-servers.md#4-world-persistence-sqlite)).

## 5. Player-count limits (summary)

| Scope | Limit | Basis |
|-------|-------|-------|
| One official server | 50 at launch | CPU tick budget, client rendering, map density. Raised only after the M10 load test |
| One community server | 50 default, up to 100 experimental | Host hardware ([06 §7.2](06-dedicated-servers.md#72-hardware-guidance-to-be-confirmed-by-m10-load-tests)) |
| Whole game | No hard limit: more servers | Stateless backend; per-server isolation |
| Backend (initial setup) | Thousands of concurrent players on one 4 vCPU VM (estimate) | Verified by the k6 load test in M10 |

These are starting points to be checked by measurement, not promises.
