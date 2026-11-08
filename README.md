# KINDLING (working title)

An original first-person multiplayer survival game for iPhone: gather, craft, build, raid and survive in
persistent worlds. The game comes with a full ecosystem around it: authoritative dedicated servers,
community-hosted servers, a backend platform and a web admin console.

> **Status:** M0 foundations done (Unity CI waits for a license secret). **M2 backend done**: accounts,
> Sign in with Apple, sessions, cloud saves, bootstrap/version gate, staff console API with TOTP +
> RBAC + audit log, server registry, server link, server browser and join tickets — plus the
> **staff web console** (`apps/web`, served at `/admin/`).
> **No gameplay yet** — that starts in M1 on the Unity side.
> Status: [`docs/milestones/M0.md`](docs/milestones/M0.md), [`docs/milestones/M2.md`](docs/milestones/M2.md).

## Quick start (backend)

```bash
pnpm install
pnpm infra:up            # PostgreSQL 17 + Redis 7.4 in Docker
cp .env.example .env
pnpm db:migrate
pnpm dev                 # http://localhost:8080/health/ready
pnpm test                # unit tests
pnpm --filter @kindling/api staff:invite -- --email you@studio.example --name You --role super_admin
pnpm --filter @kindling/api fake-server -- --token <server token>   # stand-in game server
```

API reference: [`docs/api/openapi.json`](docs/api/openapi.json). Protocols for game servers:
[`docs/protocol/`](docs/protocol/).

Unity project: see [`game/README.md`](game/README.md). Pure C# tests: `dotnet test sim/Kindling.Sim.Tests`.

**iPhone build:** the `ios-testflight` workflow builds the app on GitHub (Unity on Linux → signed `.ipa`
on macOS → TestFlight) once your Apple and Unity accounts are connected —
see [`docs/guides/ios-testflight.md`](docs/guides/ios-testflight.md).

**Start here → [`docs/blueprint/README.md`](docs/blueprint/README.md)**

| # | Document |
|---|----------|
| — | [Blueprint index, executive summary and key decisions](docs/blueprint/README.md) |
| 01 | [Game design overview](docs/blueprint/01-game-design.md) |
| 02 | [Technology stack and engine selection](docs/blueprint/02-technology-stack.md) |
| 03 | [System architecture](docs/blueprint/03-system-architecture.md) |
| 04 | [Client architecture and iPhone controls](docs/blueprint/04-client-and-ios-controls.md) |
| 05 | [Multiplayer networking](docs/blueprint/05-multiplayer-networking.md) |
| 06 | [Dedicated and community servers](docs/blueprint/06-dedicated-servers.md) |
| 07 | [Backend platform](docs/blueprint/07-backend.md) |
| 08 | [Database design](docs/blueprint/08-database.md) |
| 09 | [Admin console and server owner portal](docs/blueprint/09-admin-panel.md) |
| 10 | [iOS distribution, updates and live ops](docs/blueprint/10-ios-distribution-and-liveops.md) |
| 11 | [Seasonal content system and the Winter Update](docs/blueprint/11-seasonal-content-and-winter-update.md) |
| 12 | [Repository folder structure](docs/blueprint/12-folder-structure.md) |
| 13 | [Software, hardware and accounts you need](docs/blueprint/13-software-and-accounts.md) |
| 14 | [Milestone roadmap](docs/blueprint/14-roadmap.md) |
| 15 | [Risks, challenges and realistic limitations](docs/blueprint/15-risks-and-limitations.md) |

## Originality

KINDLING takes inspiration from genres and from the play styles of existing games: immersive, physical,
social survival, plus open-world gather/craft/build/raid survival. Every system, asset, name and line of
code in this project is original or properly licensed. We do not use code, assets, names, branding, maps
or other protected content from any existing game.
