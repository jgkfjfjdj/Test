# 12 — Repository Folder Structure

One **monorepo** holds everything: the Unity project, backend, web console, server host tooling,
infrastructure and docs. Shared contracts stay in sync, and one PR can change the protocol on both
sides. The folders are created in Milestone 0. Git doesn't track empty folders, so each one appears
when its first real file is committed.

```
/                                   repository root
├── README.md
├── CONTRIBUTING.md                 coding standards, branching, commit conventions
├── LICENSE                         (proprietary — to be decided)
├── .editorconfig
├── .gitattributes                  Git LFS for binaries (*.fbx, *.png, *.psd, *.wav, …); scenes/prefabs stay
│                                   text (Force Text serialization) + UnityYAMLMerge for merges
├── .gitignore                      Unity Library/Temp/Logs, node_modules, build outputs, .env
├── package.json                    root scripts (lint, test, build) — pnpm workspace
├── pnpm-workspace.yaml             apps/*, packages/*, tools/*
├── tsconfig.base.json
│
├── docs/
│   ├── blueprint/                  ← this blueprint
│   ├── adr/                        architecture decision records (0001-engine.md, 0002-netcode.md, …)
│   ├── api/                        generated OpenAPI spec (openapi.json) + rendered docs
│   ├── runbooks/                   ops procedures: deploy, rollback, restore DB, incident response, wipe day
│   ├── hosting/                    community server hosting guide (published to the docs site)
│   └── licenses/                   third-party asset/library license records
│
├── game/                           ── Unity 6 LTS project (iOS client + Linux dedicated server)
│   ├── Assets/
│   │   ├── Kindling/
│   │   │   ├── Runtime/
│   │   │   │   ├── Core/                 Kindling.Core.asmdef        (pure C#)
│   │   │   │   ├── Content/              Kindling.Content.asmdef     (definition types, ContentDatabase)
│   │   │   │   ├── Simulation/           Kindling.Simulation.asmdef  (rules: inventory, crafting, damage,
│   │   │   │   │                                                       stability, survival, placement)
│   │   │   │   ├── Net/                  Kindling.Net.asmdef         (protocol, chunk replication, tickets,
│   │   │   │   │                                                       FishNet adapters, lag compensation)
│   │   │   │   ├── Gameplay/             Kindling.Gameplay.asmdef    (player, entities, world, interaction,
│   │   │   │   │                                                       building, weather/time, AI)
│   │   │   │   ├── Server/               Kindling.Server.asmdef      (authority, persistence/SQLite, server link,
│   │   │   │   │                                                       admin commands, anti-cheat, events)
│   │   │   │   ├── Client/               Kindling.Client.asmdef      (input/touch, camera, HUD, HUD editor, UI
│   │   │   │   │                                                       screens, audio, VFX, settings, quality tiers)
│   │   │   │   ├── Backend/              Kindling.Backend.asmdef     (REST/WS client, generated DTOs)
│   │   │   │   └── Platform/             Kindling.Platform.asmdef    (iOS bridges, C# side)
│   │   │   ├── Editor/                   Kindling.Editor.asmdef      (content validator/exporter, build pipeline,
│   │   │   │                                                          map tools, greybox generators, bot launcher)
│   │   │   ├── Content/                  ScriptableObject assets
│   │   │   │   ├── Items/  Recipes/  Building/  Loot/  Survival/  Weather/  Biomes/  Events/
│   │   │   │   └── Seasons/Winter/       season pack definitions
│   │   │   ├── Art/                      Models/ Materials/ Textures/ Shaders/ Animations/ VFX/ (LFS)
│   │   │   ├── Audio/                    SFX/ Music/ Ambience/ (LFS)
│   │   │   ├── Prefabs/                  Player/ Entities/ Building/ Deployables/ Props/ UI/
│   │   │   ├── Scenes/                   Boot.unity, MainMenu.unity, Game.unity, Sandbox/*.unity
│   │   │   ├── Maps/                     Reach2k/ (terrain tiles, map definition, spawn/loot markers)
│   │   │   ├── UI/                       sprites, fonts (TMP), UI prefabs
│   │   │   ├── Settings/                 URP assets per tier, Input Actions, Quality presets
│   │   │   ├── Localization/             string tables
│   │   │   └── AddressableAssetsData/
│   │   ├── Plugins/iOS/                  Objective-C/Swift bridges (haptics, thermal, App Attest, SiwA, push)
│   │   ├── ThirdParty/                   vendored libraries (FishNet) + VERSIONS.md + license files
│   │   └── Tests/
│   │       ├── EditMode/                 Kindling.Tests.EditMode.asmdef
│   │       └── PlayMode/                 Kindling.Tests.PlayMode.asmdef
│   ├── Packages/manifest.json            pinned UPM packages
│   ├── ProjectSettings/                  (ProjectVersion.txt pins the Unity version)
│   └── BuildConfigs/                     build profiles (dev/staging/prod), iOS export options
│
├── sim/                            ── .NET solution for fast, Unity-free tests
│   ├── Kindling.Sim.csproj               links the pure-C# sources from game/Assets/Kindling/Runtime/{Core,Content,Simulation}
│   └── Kindling.Sim.Tests/               xUnit tests (inventory ops, crafting, stability, damage, serializers)
│
├── apps/
│   ├── api/                        ── Fastify REST + WebSockets
│   │   ├── src/
│   │   │   ├── main.ts  app.ts  config/  plugins/ (auth, rate-limit, audit, errors, openapi)
│   │   │   ├── modules/            auth/ accounts/ profiles/ social/ servers/ link/ tickets/ matchmaking/
│   │   │   │                       moderation/ liveops/ seasons/ versions/ stats/ entitlements/
│   │   │   │                       notifications/ analytics/ admin/ health/
│   │   │   ├── ws/                 realtime gateway, server link hub, admin live
│   │   │   └── lib/                crypto (jwt, argon2), redis, db, mailer, apns, apple
│   │   ├── test/                   integration tests (Testcontainers)
│   │   └── Dockerfile
│   ├── worker/                     ── BullMQ processors + schedules
│   │   └── src/jobs/               email, push, analytics-rollup, stats-aggregate, account-purge,
│   │                               reachability-probe, liveops-scheduler, metrics-downsample
│   ├── web/                        ── React admin console + owner portal (see 09 §7)
│   └── host/                       ── kindling-host: supervisor/updater (community) + fleet agent (official)
│       └── src/                    supervise/, update/ (manifest, verify, rollback), fleet/, logs/
│
├── packages/
│   ├── contracts/                  TypeBox schemas, error codes, analytics event catalogue,
│   │                               server-link message schemas, server-config JSON Schema
│   ├── db/                         Drizzle schema, SQL migrations, seed scripts
│   ├── content/                    generated content-export.json (items, recipes, loot) for backend/web
│   └── config/                     shared ESLint/Prettier/TSConfig presets
│
├── tools/
│   ├── codegen/                    OpenAPI + server-link schemas → C# DTOs for Unity
│   ├── loadtest/                   bot fleet orchestration (headless Unity clients), k6 API scenarios
│   ├── content/                    content validation in CI (reads content export)
│   └── scripts/                    dev helpers (bootstrap, reset-db, make-staff-user)
│
├── infra/
│   ├── docker/                     Dockerfiles for game-server and host images
│   ├── compose/                    docker-compose.dev.yml, docker-compose.prod.yml, .env.example
│   ├── caddy/                      Caddyfile
│   ├── observability/              prometheus/, alertmanager/, grafana/dashboards/, loki/, promtail/
│   ├── postgres/                   init scripts, pgbackrest config
│   └── terraform/                  (later) hosts, DNS, buckets
│
└── .github/
    ├── workflows/
    │   ├── ci-ts.yml               lint, typecheck, unit + integration tests (api, worker, web, host, packages)
    │   ├── ci-sim.yml              dotnet test for pure-C# simulation rules
    │   ├── ci-unity.yml            GameCI: EditMode/PlayMode tests + Linux dedicated server build
    │   ├── build-images.yml        Docker images → GHCR
    │   ├── ios-testflight.yml      macOS: Unity → Xcode → Fastlane → TestFlight (manual trigger / tags)
    │   └── release-server.yml      sign server build, upload to storage, register manifest (draft)
    ├── CODEOWNERS
    └── pull_request_template.md
```

**Naming conventions**
- C#: namespaces `Kindling.<Assembly>.<Area>`; one class per file; `PascalCase` types and methods,
  `_camelCase` private fields.
- TypeScript: `kebab-case` files, `camelCase` symbols, ESM modules.
- Content IDs: `item.<category>.<name>`, `recipe.<name>`, `piece.<name>`, `loot.<table>`,
  `season.<key>`.
- Branches: `feature/<milestone>-<topic>`, `fix/<topic>`. Releases tagged `client/vX.Y.Z`,
  `server/vX.Y.Z`, `backend/vX.Y.Z`.
