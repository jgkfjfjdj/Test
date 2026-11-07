# 09 — Admin Console and Server Owner Portal

One responsive web application (`apps/web`) with **two separate areas**:

| Area | Users | Sign-in | Route prefix |
|------|-------|---------|--------------|
| **Staff Console** | Studio staff (admins, moderators, live ops, support, analysts) | Staff account: email + password + **required TOTP** | `/staff/*` |
| **Owner Portal** | Community server owners and their server staff | Player account (email/password or Sign in with Apple via web) | `/servers/*` |

The two areas share UI components but use **different session cookies and different backend
authorization**. A player account can never call staff endpoints, whatever the UI shows.

## 1. Principles

1. **No mock data in production builds.** A page or button ships only when its backend endpoint
   exists and is tested. Unfinished features are hidden behind feature flags, not faked.
2. **Every change is audited** with actor, reason, before/after and result.
3. **Least privilege:** granular permissions, roles as bundles of permissions, step-up
   re-authentication for dangerous actions.
4. **Works on phones:** layouts collapse to single columns, tables become card lists, actions sit
   behind a bottom sheet, touch targets are at least 44 px. Tested at 375 px width.
5. **Fast:** TanStack Query caching, cursor pagination, live updates through `/admin/v1/live`.

## 2. Staff authentication and sessions
- Staff accounts are created **by invitation only**. Accepting the invite sets a password and enrolls
  TOTP (QR code + recovery codes). WebAuthn/passkeys come later.
- Session: httpOnly, Secure, `SameSite=Strict` cookie; CSRF token header; 8 h absolute and 30 min idle
  timeout; one-click revocation of all sessions.
- **Step-up:** actions marked ⚠ below require a TOTP re-entry within the past 5 minutes.
- Optional IP allowlist for the Super Admin role. Alerts on new-location logins.

## 3. Roles and permissions

| Permission | Super Admin | Admin | Moderator | Support | Live Ops | Analyst | Server Ops |
|------------|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| `players.read` | ✓ | ✓ | ✓ | ✓ | | ✓ | |
| `players.edit` (names, email verification, unlink) | ✓ | ✓ | | ✓ | | | |
| `players.sanction` (warn/mute/ban) | ✓ | ✓ | ✓ | | | | |
| `players.sanction.permanent` ⚠ | ✓ | ✓ | | | | | |
| `players.inventory.read` | ✓ | ✓ | ✓ | ✓ | | | |
| `players.inventory.write` ⚠ | ✓ | ✓ | | | | | |
| `players.entitlements.grant` ⚠ | ✓ | ✓ | | ✓ | | | |
| `reports.handle` | ✓ | ✓ | ✓ | | | | |
| `moderation.filters` | ✓ | ✓ | ✓ | | | | |
| `servers.read` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `servers.command` (broadcast, kick, restart) | ✓ | ✓ | ✓ (kick/broadcast) | | | | ✓ |
| `servers.manage` (verify/delist, official fleet config) ⚠ | ✓ | ✓ | | | | | ✓ |
| `versions.manage` ⚠ | ✓ | ✓ | | | | | ✓ |
| `content.publish` ⚠ | ✓ | ✓ | | | ✓ | | |
| `liveops.announcements` | ✓ | ✓ | | | ✓ | | |
| `liveops.events` / `liveops.seasons` | ✓ | ✓ | | | ✓ | | |
| `liveops.config` ⚠ | ✓ | ✓ | | | ✓ | | |
| `liveops.maintenance` ⚠ | ✓ | ✓ | | | | | ✓ |
| `analytics.read` | ✓ | ✓ | | | ✓ | ✓ | |
| `audit.read` | ✓ | ✓ | | | | | |
| `system.db_monitor` | ✓ | ✓ | | | | | ✓ |
| `staff.manage` ⚠ | ✓ | | | | | | |

Roles are data (`staff_roles.permissions`), so new roles need no code change. The backend checks
permissions on every route; the UI hides what you can't use, but that's for convenience only.

## 4. Staff Console pages → backend endpoints

| Page | What it does | Backend (all under `/admin/v1`) | Milestone |
|------|--------------|----------------------------------|-----------|
| **Dashboard** | Live CCU, servers online, logins/min, error rate, open reports, active maintenance or events | `GET /dashboard/summary`, WS `/live` | M7 |
| **Players: search** | By name, email, account ID, device. Status filters | `GET /players?query=` | M7 |
| **Player detail** | Profile, status, linked logins, devices, sessions, current server, stats, sanctions, reports by and against, entitlements, notes, audit trail | `GET /players/{id}`, `/players/{id}/sessions`, `/sanctions`, `/reports`, `/entitlements`, `/audit` | M7 |
| **Sanctions** | Warn / mute / ban (scope, duration, reason, evidence); revoke | `POST /players/{id}/sanctions`, `POST /sanctions/{id}/revoke` | M7 |
| **Inventory moderation** | Pick server → live inventory, equipment and owned containers of a player (online or sleeping); add/remove/move items (item picker from the content catalog, reason required) ⚠ | `GET /servers/{sid}/players/{aid}/inventory`, `POST …/inventory/ops` (relayed over the server link) | M7 |
| **Entitlements** | Grant or revoke cosmetics (support cases) ⚠ | `POST /players/{id}/entitlements`, `DELETE …/{key}` | M9 |
| **Reports queue** | Triage, assign, view evidence (chat excerpt, context), resolve with an action | `GET /reports`, `PATCH /reports/{id}` | M7 |
| **Anti-cheat flags** | Review flags, link to the player, dismiss or act | `GET /anticheat/flags`, `PATCH …/{id}` | M10 |
| **Text filters** | Manage blocked/masked terms; test a string | `GET/POST/DELETE /moderation/filters`, `POST /moderation/filters/test` | M7 |
| **Servers: list** | Official + community; live state; filters; verify/delist ⚠ | `GET /servers`, `PATCH /servers/{id}/listing` | M8 |
| **Server detail** | Live metrics (charts), players online, config summary, wipe schedule, staff, ban list, event log, **live log tail**; commands: broadcast, kick, restart (with delay), save, shutdown ⚠ | `GET /servers/{id}`, `/metrics`, `/players`, `/logs` (WS), `POST /servers/{id}/commands` | M8 |
| **Official fleet** | Hosts, desired state, builds per server, canary rollout | `GET/PUT /fleet/hosts`, `/fleet/servers` | M10 |
| **Versions** | Client versions & statuses; version policy (min/latest build) ⚠ | `GET/POST /versions/client`, `PUT /versions/policy` | M9 |
| **Server builds** | Upload manifest, publish to channel, mark mandatory, withdraw ⚠ | `GET/POST /builds/server`, `POST …/{id}/publish` | M8 |
| **Content releases** | Register an Addressables catalog, set the compatible build range, staged rollout %, roll back ⚠ | `GET/POST /content/releases`, `PATCH …/{id}` | M9 |
| **Announcements** | Create, localize, target, schedule and preview (phone-frame preview) | `GET/POST/PATCH /announcements` | M9 |
| **Events** | Schedule multiplier, world and challenge events; target servers; live status | `GET/POST/PATCH /events` | M9 |
| **Seasons & challenges** | Season schedule, linked content pack, challenges and rewards | `GET/POST/PATCH /seasons`, `/challenges` | M9 |
| **Remote config** | Key editor with JSON schema hints, targeting, history and diff, rollback ⚠ | `GET/PUT /config/{key}`, `GET /config/{key}/history` | M9 |
| **Maintenance** | Schedule or start immediately ⚠; scope; localized message; cancel | `GET/POST/PATCH /maintenance` | M9 |
| **Analytics** | DAU/WAU/MAU, retention cohorts, session length, CCU by region/server, crashes per session, FPS by device tier, funnel (install → login → first join → day 1) | `GET /analytics/*` (from `daily_metrics`) | M9 |
| **Player statistics** | Leaderboards (official scope), stat distributions | `GET /stats/leaderboards`, `/stats/distribution` | M9 |
| **Audit log** | Filter by actor, action, target, time; view diffs; CSV export | `GET /audit`, `GET /audit/export` | M2 (basic), M9 (full) |
| **Database** | PostgreSQL: connections, cache hit ratio, top slow queries, table and index sizes, locks, replication lag. Redis: memory, ops/s, keyspace, BullMQ queue depths. **Read-only** | `GET /system/db`, `/system/redis`, `/system/queues` | M9 |
| **System health** | API instances, worker status, server link counts, versions, recent deploys | `GET /system/health` | M9 |
| **Staff** | Invite, assign roles, disable, reset TOTP ⚠, view sessions | `GET/POST/PATCH /staff` | M2 |

## 5. Audit logging enforcement
- Every mutating admin route is wrapped in `withAudit(action, {targetFrom, requireReason})`. The
  middleware writes the audit record **in the same transaction** as the change (or, for server-link
  commands, writes `pending` and then completes it with the result).
- A reason is required for sanctions, inventory operations, entitlement grants, config changes,
  maintenance and listing changes.
- `audit_log` is append-only at the database-grant level. Any export is itself audited.

## 6. Owner Portal (community servers)

| Page | Capabilities | Backend |
|------|--------------|---------|
| My servers | List of owned and staffed servers with live state | `GET /admin/v1/owned-servers` |
| Create server | Name, region, mode, tags → server token shown once | `POST /admin/v1/owned-servers` |
| Overview | Live players, tick time, memory, uptime, version, update status; charts (30 days) | `GET …/{id}`, `…/{id}/metrics` |
| Settings | Edit the config subset (name, description, rules, rates, raid/sleeper rules, wipe schedule, password, update window); shows which changes need a restart | `PATCH …/{id}/config` → server link `config_update` |
| Players | Online and recent players; kick, mute, server-ban, view/edit inventory (owner/admin), teleport | `GET …/{id}/players`, `POST …/{id}/commands` |
| Bans | Server ban list; add or remove | `GET/POST/DELETE …/{id}/bans` |
| Staff | Add or remove server admins and moderators (by player name) | `GET/POST/DELETE …/{id}/staff` |
| Logs | Live tail, filters, download recent files | WS `…/{id}/logs` |
| Controls | Restart (with countdown and message), scheduled restarts, save now, shutdown, broadcast, wipe now / schedule wipe | `POST …/{id}/commands` |
| Updates | Current build, available build, auto-update settings, update history, rollback status | `GET …/{id}/updates` |
| Security | Rotate server token, delete server | `POST …/{id}/token/rotate`, `DELETE …/{id}` |
| Audit | Everything done by server staff on this server | `GET …/{id}/audit` |

Owners can use these powers **only on their own servers**. Platform-wide sanctions and inventory
changes on official servers are staff-only.

## 7. Frontend structure

```
apps/web/src/
  app/              router, providers, layout shells (staff, owner)
  features/         one folder per page group: players/, servers/, reports/, liveops/, versions/, …
    <feature>/api.ts         typed calls (generated client) + query keys
    <feature>/components/
    <feature>/routes.tsx
  components/       shared UI (DataTable → CardList on mobile, forms, charts, ConfirmWithReason, StepUpDialog)
  lib/              auth, permissions (can()), live socket, formatting, i18n
```

Testing: Vitest + Testing Library for components; Playwright end-to-end tests against a seeded local
backend (desktop and iPhone viewports) for critical flows: login + TOTP, ban player, inventory op,
publish announcement, start maintenance.
