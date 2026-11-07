# Contributing to KINDLING

## Ground rules
- **Original work only.** No code, assets, names or designs copied from other games
  (docs/blueprint/01-game-design.md §3). Third-party assets/libraries need a commercial-use license,
  recorded in `docs/licenses/`.
- **Server authority.** The client never decides outcomes; every intent is validated on the server.
- **Done means demonstrated.** A feature is done when its milestone exit criteria are shown working.
  Unfinished work is labeled as such, never faked (no mock data in production paths).

## Branches, commits and PRs
- Branches: `feature/<milestone>-<topic>`, `fix/<topic>`, `docs/<topic>`.
- Commits: imperative mood, explain *why* in the body when it isn't obvious.
- Every PR: CI green, tests for new behavior, docs/ADR updated when a decision changes.
- Release tags: `client/vX.Y.Z`, `server/vX.Y.Z`, `backend/vX.Y.Z`.

## TypeScript (apps/, packages/)
```bash
pnpm install
pnpm infra:up                 # PostgreSQL 17 + Redis 7.4 (Docker)
cp .env.example .env
pnpm db:migrate
pnpm dev                      # API on http://localhost:8080 (try /health/ready)
pnpm lint && pnpm format:check && pnpm typecheck && pnpm test
TEST_DATABASE_URL=postgres://kindling:kindling@localhost:5432/kindling_test \
TEST_REDIS_URL=redis://localhost:6379/15 pnpm test:integration
pnpm openapi                  # regenerate docs/api/openapi.json after API changes (CI checks it)
pnpm --filter @kindling/api staff:invite -- --email you@studio.example --name You --role super_admin
pnpm --filter @kindling/api fake-server -- --token kst_...   # stand-in game server for local testing
pnpm --filter @kindling/api keys:generate                    # production keys/secrets (never commit)
```
- Security-relevant decisions are recorded in ADRs (see ADR 0006 for auth/session/trust).
- Staff mutations must be audited (`AuditService.recordStaff`, same transaction) and take a `reason`.
- Strict TypeScript, ESM, `import type` for types, no `console` in services (use the request logger).
- Route schemas use TypeBox; errors use `ProblemError` with a code from `@kindling/contracts`.

## C# (game/, sim/)
- C# 9 (Unity 6). No `record`, `init`, or file-scoped namespaces.
- Pure assemblies (`noEngineReferences: true`) must not use UnityEngine; they are also built by `sim/`.
- Tests in `game/Assets/Kindling/Tests/EditMode/Pure` run in Unity **and** with
  `dotnet test sim/Kindling.Sim.Tests`.
- Namespaces `Kindling.<Assembly>.<Area>`. Don't name a namespace after a common type
  (`Math`, `Time`, `Debug`, `Random`): it hides that type in sibling namespaces.
- Zero GC allocations per frame in steady-state gameplay code.

## Unity assets
- Scenes/prefabs are text (Force Text); binaries go through Git LFS (`git lfs install` once).
- Commit `.meta` files with their assets. Never commit `Library/`, `Temp/`, `Builds/`.
