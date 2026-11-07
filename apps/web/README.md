# apps/web — KINDLING staff console

React single-page app for studio staff. In production it is **served by the API under `/admin/`**
(`ADMIN_WEB_DIR`), on the same origin as the staff API (`/admin/v1`), so the session cookie can be
`HttpOnly`, `Secure`, `SameSite=Strict` and scoped to `Path=/admin`.

| Page | Permission | Backend |
|------|------------|---------|
| Sign in (password + authenticator code), invitation setup (QR / tap-to-open) | — | `/admin/v1/auth/*` |
| Overview (servers online, players, maintenance, backend build) | per card | servers, maintenance, `/health/info` |
| Servers: list, register (token shown once), detail with live metrics, commands, listing, token rotation | `servers.read` / `servers.command` / `servers.manage` | `/admin/v1/servers*` |
| Maintenance: schedule / start / end | `liveops.maintenance` | `/admin/v1/maintenance*` |
| Remote config: create, edit (JSON validated), delete | `liveops.config` | `/admin/v1/config*` |
| App versions: minimum / latest build, store URL | `versions.manage` | `/admin/v1/version-policy*` |
| Staff: invite, roles, disable, sign out everywhere, reset credentials | `staff.manage` | `/admin/v1/staff*` |
| Audit log: filters, before/after diffs, pagination | `audit.read` | `/admin/v1/audit` |

Player management, reports and sanctions arrive with M7 (their API doesn't exist yet).

## Develop

```bash
pnpm infra:up && cp .env.example .env && pnpm db:migrate   # once
pnpm dev                                  # API on :8080 (ADMIN_COOKIE_SECURE=false in .env)
pnpm --filter @kindling/web dev           # console on http://localhost:5173/admin/ (proxies API calls)
pnpm --filter @kindling/api staff:invite -- --email you@studio.example --name You --role super_admin
# open the printed link, replacing http://localhost:8080 with http://localhost:5173
```

The typed API client is generated from `docs/api/openapi.json`:
`pnpm --filter @kindling/web api:types` (CI fails if `src/api/schema.d.ts` is stale).

## Test

```bash
pnpm --filter @kindling/web test        # Vitest + Testing Library (client, step-up flow, helpers)
pnpm --filter @kindling/web build
pnpm --filter @kindling/web e2e         # Playwright: real API + DB; desktop and iPhone viewports
```

The e2e run **wipes** `TEST_DATABASE_URL` / `TEST_REDIS_URL` (it refuses databases without
"test" or "e2e" in the name). Screenshots go to `e2e/screenshots/` (or `E2E_SCREENSHOT_DIR`).

## Security notes
- No tokens in `localStorage`: the session is an httpOnly cookie; the CSRF token lives in memory
  and is re-read from `/admin/v1/auth/me` after a reload.
- Strict Content-Security-Policy (no inline scripts or styles, no framing) set by the API.
- Dangerous writes pause for an authenticator code (step-up) and retry automatically.
- Every write asks for a reason, which is stored in the audit log.
