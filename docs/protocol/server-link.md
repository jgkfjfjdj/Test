# Server link — game server ↔ backend protocol (v1)

WebSocket, JSON text frames, at `wss://<api>/server/v1/link`.
Schemas: `packages/contracts/src/server-link.ts` (source of truth). Reference client:
`apps/api/dev/fake-game-server.ts` (also usable as a CLI: `pnpm --filter @kindling/api fake-server`).

## Connecting

- `Authorization: Bearer <server token>` on the upgrade request. Tokens start with `kst_`, are
  created by staff (official servers) or owners (M8), and are shown once. Invalid → HTTP 401.
- One link per server: a newer connection replaces the older one (close code 4000).

## Envelope

`{ "t": "<type>", "id": "<request id>"?, "re": "<id being answered>"?, "d": { … } }`

## Messages

| Direction | `t` | Payload | Notes |
|-----------|-----|---------|-------|
| S→B | `hello` (with `id`) | build, protocol, host, ports, maxPlayers, passwordHash (argon2id or null), mapId, mode, tags, configSummary, lastWipeAt, nextWipeAt | Must be the first message, within 10 s |
| B→S | `welcome` (`re`) | `serverId`, `ticketIssuer`, `jwks`, `heartbeatIntervalSeconds` (10) | |
| S→B | `heartbeat` | players, queue, tickMsAvg, tickMsP99, fps, memoryMb, entities, uptimeSeconds | Every 10 s. Live state expires after 30 s without one |
| S→B | `player_joined` (with `id`) | `accountId`, `jti` | After offline ticket verification |
| B→S | `player_joined_ack` (`re`) | `accountId`, `ok`, `reason?` | `ok:false` → disconnect the player |
| S→B | `player_left` | `accountId`, `reason` | |
| B→S | `cmd` (with `id`) | `name` (`broadcast` / `kick` / `save` / `restart`), `args` | |
| S→B | `cmd_result` (`re`) | `ok`, `result?`, `error?` | Answer within 10 s |
| B→S | `error` (`re?`) | `code`, `message` | Protocol violations; 5 → disconnect |

## Close codes

| Code | Meaning |
|------|---------|
| 4000 | Replaced by a newer connection for the same server |
| 4002 | Protocol violation (bad first message, malformed or invalid messages) |
| 4003 | Token revoked / server delisted — reconnecting with the same token will fail |
| 4004 | No `hello` within 10 s |
| 1001 | Backend shutting down — reconnect with backoff |

The backend also sends WebSocket pings every 20 s; a missed pong terminates the connection.
Game servers reconnect with exponential backoff (1, 2, 4 … 30 s) and send `hello` again.
