# ADR 0003 — Node.js + TypeScript + Fastify modular monolith

- **Status:** Accepted (2026-11-07)
- **Details:** docs/blueprint/07-backend.md

## Decision
- Node.js 24 LTS (CI and containers; 22.12+ works for local development), TypeScript 6 (strict).
  TypeScript 7 (native compiler) is not adopted yet because typescript-eslint supports `< 6.1`.
- Fastify 5 with TypeBox 1.x schemas (validation + OpenAPI 3.1 from the same source).
- Drizzle ORM + SQL migrations in `packages/db/migrations`; postgres.js driver (3.x; 4.x was released
  days ago and is deferred until it has matured).
- ioredis 5 for Redis.
- pnpm workspace monorepo. Internal packages (`@kindling/*`) export TypeScript source; the API is
  bundled with esbuild for production (npm dependencies stay external and are installed by
  `pnpm deploy --prod` in the image).
- Errors are RFC 9457 problem+json with stable codes; health endpoints at `/health/{live,ready,info}`.

## Consequences
- One deployable (`api`, later `worker`) keeps operations simple; module boundaries allow a later split.
- The OpenAPI document (`docs/api/openapi.json`) is generated and checked in; CI fails if it is stale.
  C# DTOs for Unity are generated from it (from M2).
