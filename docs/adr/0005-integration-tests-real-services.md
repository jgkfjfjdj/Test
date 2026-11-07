# ADR 0005 — Integration tests run against real PostgreSQL and Redis services

- **Status:** Accepted (2026-11-07). Changes the blueprint's original "Testcontainers" plan.

## Context
Integration tests must use real PostgreSQL and Redis (no mocks for the database layer).
Testcontainers starts containers from inside the test process, which needs a Docker daemon wherever
tests run and adds startup time to every run.

## Decision
- Unit tests (`*.test.ts`) need no services and run everywhere with `pnpm test`.
- Integration tests (`*.integration.test.ts`) run with `pnpm test:integration` against services given
  by `TEST_DATABASE_URL` / `TEST_REDIS_URL`, and **fail loudly** if those are missing.
  - Locally: `pnpm infra:up` (Docker Compose creates the separate `kindling_test` database).
  - CI: GitHub Actions service containers (`postgres:17-alpine`, `redis:7.4-alpine`).
- Tests that need a clean database reset the objects they own; the test database is never the dev database.

## Consequences
- Same images and versions locally and in CI; faster runs; no Docker-in-test dependency.
- Developers must start the dev stack before running integration tests.
