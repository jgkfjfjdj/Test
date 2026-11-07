// Development stand-in for the Unity dedicated server. Speaks the real server-link protocol so the
// backend can be exercised end to end without Unity:
//   pnpm --filter @kindling/api fake-server -- --token kst_... [--url http://localhost:8080] [--players 3]
// Paste a join ticket (from POST /v1/servers/{id}/join) on stdin to admit a player.
import { createInterface } from 'node:readline';
import { parseArgs } from 'node:util';
import { FakeGameServer } from '../dev/fake-game-server.js';
import { HEARTBEAT_INTERVAL_SECONDS } from '../src/modules/servers/link-routes.js';

const { values } = parseArgs({
  // pnpm forwards a literal '--' separator; drop it so both `pnpm x -- --flag` and `pnpm x --flag` work.
  args: process.argv.slice(2).filter((arg) => arg !== '--'),
  options: {
    token: { type: 'string' },
    url: { type: 'string', default: 'http://localhost:8080' },
    players: { type: 'string', default: '0' },
    host: { type: 'string', default: '127.0.0.1' },
    'max-players': { type: 'string', default: '50' },
  },
});
if (!values.token) {
  console.error('Usage: fake-server -- --token <server token> [--url http://localhost:8080] [--players N]');
  process.exit(2);
}

let players = Number(values.players);
const server = await FakeGameServer.connect(values.url!, values.token, {
  host: values.host!,
  maxPlayers: Number(values['max-players']),
  buildVersion: '0.0.0-fake',
});
console.log(
  `Linked as server ${server.serverId}. Heartbeat every ${HEARTBEAT_INTERVAL_SECONDS}s. Paste join tickets to admit players.`,
);

server.commandHandler = (name, args) => {
  console.log(`← command ${name} ${JSON.stringify(args)}`);
  return { ok: true, result: { handledBy: 'fake-game-server', name } };
};

const beat = () => server.heartbeat({ players, uptimeSeconds: Math.round(process.uptime()) });
await beat();
const timer = setInterval(() => void beat(), HEARTBEAT_INTERVAL_SECONDS * 1000);

const rl = createInterface({ input: process.stdin });
rl.on('line', (line) => {
  const ticket = line.trim();
  if (!ticket) return;
  server
    .admit(ticket)
    .then(({ ticket: t, ack }) => {
      if (ack.ok) players += 1;
      console.log(
        `${ack.ok ? 'admitted' : `rejected by backend (${ack.reason})`}: ${t.displayName} (${t.accountId})`,
      );
    })
    .catch((err: unknown) =>
      console.log(`ticket refused locally: ${err instanceof Error ? err.message : String(err)}`),
    );
});

const stop = () => {
  clearInterval(timer);
  rl.close();
  server.close();
};
process.on('SIGINT', stop);
process.on('SIGTERM', stop);
server.ws.on('close', (code, reason) => {
  console.log(`Link closed (${code} ${reason.toString()})`);
  clearInterval(timer);
  rl.close();
});
