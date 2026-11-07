import { maintenanceWindows, newId } from '@kindling/db';
import { generateKeyPair, SignJWT } from 'jose';
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest';
import { hashPassword } from '../src/lib/passwords.js';
import { TicketRejected, TicketVerifier } from '../src/modules/servers/ticket-verifier.js';
import { FakeGameServer, type DEFAULT_HELLO } from '../dev/fake-game-server.js';
import { createHarness, type Harness } from './support/harness.js';
import { bearer, registerPlayer, type TestPlayer } from './support/players.js';
import { createStaff, nextCode, staffCookies, staffHeaders, type TestStaff } from './support/staff.js';

let h: Harness;
let baseUrl: string;
let admin: TestStaff;
const open: FakeGameServer[] = [];

beforeAll(async () => {
  h = await createHarness({ SERVER_COMMAND_TIMEOUT_MS: '500' });
  baseUrl = await h.app.listen({ host: '127.0.0.1', port: 0 });
  admin = await createStaff(h, ['super_admin']);
});
afterEach(() => {
  while (open.length) open.pop()!.close();
});
afterAll(async () => {
  await h.close();
});

async function createServer(overrides: Record<string, unknown> = {}): Promise<{ id: string; token: string }> {
  // Keep the admin's step-up fresh (the test clock moves forward during other tests).
  await h.inject({
    method: 'POST',
    url: '/admin/v1/auth/step-up',
    cookies: staffCookies(admin),
    headers: staffHeaders(admin),
    payload: { totp: nextCode(h, admin.secret) },
  });
  const res = await h.inject({
    method: 'POST',
    url: '/admin/v1/servers',
    cookies: staffCookies(admin),
    headers: staffHeaders(admin),
    payload: {
      name: `Official EU ${newId().slice(-6)}`,
      region: 'eu-west',
      maxPlayers: 50,
      mode: 'pvp',
      tags: ['official'],
      reason: 'launch fleet',
      ...overrides,
    },
  });
  expect(res.statusCode, res.body).toBe(201);
  return { id: res.json().server.id, token: res.json().token };
}

async function linked(token: string, hello: Partial<typeof DEFAULT_HELLO> = {}): Promise<FakeGameServer> {
  // Game servers verify tickets with the same (test) clock the backend uses to issue them.
  const server = await FakeGameServer.connect(baseUrl, token, hello, () => h.clock.now().getTime());
  open.push(server);
  return server;
}

async function join(
  p: TestPlayer,
  serverId: string,
  body: Record<string, unknown> = {},
  headers: Record<string, string> = {},
) {
  return h.inject({
    method: 'POST',
    url: `/v1/servers/${serverId}/join`,
    headers: { ...bearer(p.accessToken), 'x-protocol': '1', ...headers },
    payload: body,
  });
}

describe('server registration and link', () => {
  it('returns the token once, stores only its hash, and refuses bad tokens before upgrading', async () => {
    const { id, token } = await createServer();
    expect(token).toMatch(/^kst_[A-Za-z0-9_-]{43}$/);
    const stored = await h.db.sql<
      { token_hash: Buffer }[]
    >`SELECT token_hash FROM server_tokens WHERE server_id = ${id}`;
    expect(stored[0]?.token_hash.toString('hex')).not.toContain(token);
    await expect(FakeGameServer.open(baseUrl, 'kst_wrong')).rejects.toThrow('HTTP 401');
  });

  it('requires hello first and closes on protocol violations', async () => {
    const { token } = await createServer();
    const raw = await FakeGameServer.open(baseUrl, token);
    raw.send({ t: 'heartbeat', d: {} });
    expect((await raw.waitForClose()).code).toBe(4002);
  });

  it('handshakes, publishes the server in the browser, and drops it when the link closes', async () => {
    const { id, token } = await createServer();
    const p = await registerPlayer(h);
    const server = await linked(token, { tags: ['official', 'weekly'] });
    expect(server.serverId).toBe(id);
    expect(server.verifier).toBeDefined();

    await server.heartbeat({ players: 7 });
    const list = await h.inject({
      method: 'GET',
      url: '/v1/servers?region=eu-west',
      headers: { ...bearer(p.accessToken), 'x-protocol': '1' },
    });
    const item = list.json().items.find((i: { id: string }) => i.id === id);
    expect(item).toMatchObject({
      players: 7,
      maxPlayers: 50,
      host: 'eu1.kindling.example',
      gamePort: 27015,
      compatible: true,
      tags: ['official', 'weekly'],
    });

    const detail = await h.inject({
      method: 'GET',
      url: `/v1/servers/${id}`,
      headers: bearer(p.accessToken),
    });
    expect(detail.json()).toMatchObject({ online: true, configSummary: { gather: 1 } });

    server.close();
    await server.waitForClose();
    await new Promise((r) => setTimeout(r, 100));
    const after = await h.inject({ method: 'GET', url: '/v1/servers', headers: bearer(p.accessToken) });
    expect(after.json().items.find((i: { id: string }) => i.id === id)).toBeUndefined();
  });

  it('filters the browser and hides incompatible versions unless asked', async () => {
    const a = await createServer({ name: 'Filter Alpha' });
    const b = await createServer({ name: 'Filter Beta', mode: 'pve' });
    const p = await registerPlayer(h);
    const sa = await linked(a.token, { protocolVersion: 1 });
    const sb = await linked(b.token, {
      protocolVersion: 2,
      mode: 'pve',
      passwordHash: await hashPassword('secret-pass'),
    });
    await sa.heartbeat({ players: 50 });
    await sb.heartbeat({ players: 0 });
    const q = async (query: string) =>
      (
        await h.inject({
          method: 'GET',
          url: `/v1/servers?search=Filter${query}`,
          headers: { ...bearer(p.accessToken), 'x-protocol': '1' },
        })
      )
        .json()
        .items.map((i: { name: string }) => i.name);
    expect(await q('')).toEqual(['Filter Alpha']);
    expect(await q('&includeIncompatible=true')).toEqual(['Filter Alpha', 'Filter Beta']);
    expect(await q('&includeIncompatible=true&notFull=true')).toEqual(['Filter Beta']);
    expect(await q('&includeIncompatible=true&hasPlayers=true')).toEqual(['Filter Alpha']);
    expect(await q('&includeIncompatible=true&noPassword=true')).toEqual(['Filter Alpha']);
    expect(await q('&includeIncompatible=true&mode=pve')).toEqual(['Filter Beta']);
  });

  it('replaces an older connection for the same server', async () => {
    const { token } = await createServer();
    const first = await linked(token);
    await linked(token);
    expect((await first.waitForClose()).code).toBe(4000);
  });

  it('rotating the token disconnects the server and invalidates the old token', async () => {
    const { id, token } = await createServer();
    const server = await linked(token);
    const rotate = await h.inject({
      method: 'POST',
      url: `/admin/v1/servers/${id}/token/rotate`,
      cookies: staffCookies(admin),
      headers: staffHeaders(admin),
      payload: { reason: 'token leaked' },
    });
    expect(rotate.statusCode).toBe(200);
    expect((await server.waitForClose()).code).toBe(4003);
    await expect(FakeGameServer.open(baseUrl, token)).rejects.toThrow('HTTP 401');
    await linked(rotate.json().token);
  });

  it('serves the ticket JWKS without private key material', async () => {
    const res = await h.inject({ method: 'GET', url: '/server/v1/jwks' });
    expect(res.json().issuer).toBe('kindling-tickets');
    expect(res.json().keys[0]).toMatchObject({ kty: 'OKP', crv: 'Ed25519', alg: 'EdDSA' });
    expect(res.json().keys[0].d).toBeUndefined();
    expect(res.headers['cache-control']).toBe('public, max-age=300');
  });
});

describe('join tickets', () => {
  it('admits a player end to end: ticket → offline verification → backend acknowledgement', async () => {
    const { id, token } = await createServer();
    const server = await linked(token);
    const p = await registerPlayer(h);
    const res = await join(p, id);
    expect(res.statusCode, res.body).toBe(200);
    expect(res.json().server).toEqual({
      id,
      host: 'eu1.kindling.example',
      gamePort: 27015,
      protocolVersion: 1,
    });

    const { ticket, ack } = await server.admit(res.json().ticket);
    expect(ticket).toMatchObject({ accountId: p.accountId, displayName: p.displayName });
    expect(ack).toEqual({ accountId: p.accountId, ok: true });
    const sessions = await h.db.sql<
      { n: number }[]
    >`SELECT count(*)::int AS n FROM play_sessions WHERE account_id = ${p.accountId} AND server_id = ${id} AND left_at IS NULL`;
    expect(sessions[0]?.n).toBe(1);

    server.send({ t: 'player_left', d: { accountId: p.accountId, reason: 'logout' } });
    await new Promise((r) => setTimeout(r, 100));
    const closedSessions = await h.db.sql<
      { n: number }[]
    >`SELECT count(*)::int AS n FROM play_sessions WHERE account_id = ${p.accountId} AND left_at IS NOT NULL`;
    expect(closedSessions[0]?.n).toBe(1);
  });

  it('rejects forged, foreign-key, wrong-server, expired and replayed tickets', async () => {
    const a = await createServer();
    const b = await createServer();
    const serverA = await linked(a.token);
    const serverB = await linked(b.token);
    const p = await registerPlayer(h);
    const ticket = (await join(p, a.id)).json().ticket as string;

    // Forged: payload changed after signing.
    const [header, , signature] = ticket.split('.');
    const forgedPayload = Buffer.from(
      JSON.stringify({ iss: 'kindling-tickets', aud: a.id, sub: newId(), jti: 'x', exp: 9_999_999_999 }),
    ).toString('base64url');
    await expect(serverA.verifier!.verify(`${header}.${forgedPayload}.${signature}`)).rejects.toMatchObject({
      reason: 'invalid',
    });

    // Signed by a key that isn't in the backend JWKS.
    const { privateKey } = await generateKeyPair('EdDSA', { crv: 'Ed25519' });
    const foreign = await new SignJWT({})
      .setProtectedHeader({ alg: 'EdDSA', kid: h.keys.tickets.activeKid })
      .setIssuer('kindling-tickets')
      .setAudience(a.id)
      .setSubject(p.accountId)
      .setJti('j')
      .setIssuedAt()
      .setExpirationTime('1m')
      .sign(privateKey);
    await expect(serverA.verifier!.verify(foreign)).rejects.toMatchObject({ reason: 'invalid' });

    // Valid ticket, but for another server.
    await expect(serverB.verifier!.verify(ticket)).rejects.toMatchObject({ reason: 'wrong_server' });

    // Expired (verifier clock two minutes ahead).
    const late = new TicketVerifier(
      a.id,
      h.keys.tickets.publicJwks(),
      () => h.clock.now().getTime() + 120_000,
    );
    await expect(late.verify(ticket)).rejects.toBeInstanceOf(TicketRejected);
    await expect(late.verify(ticket)).rejects.toMatchObject({ reason: 'expired' });

    // Accepted once, then replay on the same server is refused offline…
    expect((await serverA.admit(ticket)).ack.ok).toBe(true);
    await expect(serverA.verifier!.verify(ticket)).rejects.toMatchObject({ reason: 'replayed' });

    // …and a server that lost its memory (restart) is told by the backend that the ticket was already used.
    const restarted = new TicketVerifier(a.id, h.keys.tickets.publicJwks(), () => h.clock.now().getTime());
    const again = await restarted.verify(ticket);
    const ack = await serverA.request(
      'player_joined',
      { accountId: again.accountId, jti: again.jti },
      'player_joined_ack',
    );
    expect(ack.d).toEqual({ accountId: p.accountId, ok: false, reason: 'ticket_not_issued_or_already_used' });
  });

  it('enforces capacity with reservations', async () => {
    const { id, token } = await createServer({ maxPlayers: 2 });
    const server = await linked(token, { maxPlayers: 2 });
    await server.heartbeat({ players: 1 });
    const p1 = await registerPlayer(h);
    const p2 = await registerPlayer(h);
    expect((await join(p1, id)).statusCode).toBe(200);
    const full = await join(p2, id);
    expect(full.statusCode).toBe(409);
    expect(full.json().code).toBe('server.full');
    expect((await join(p1, id)).statusCode).toBe(200); // re-joining refreshes the same reservation
    h.clock.advance(61_000); // reservation expires
    expect((await join(p2, id)).statusCode).toBe(200);
  });

  it('checks server passwords with an attempt limit', async () => {
    const { id, token } = await createServer();
    await linked(token, { passwordHash: await hashPassword('friends-only') });
    const p = await registerPlayer(h);
    expect((await join(p, id)).json().code).toBe('server.password_required');
    expect((await join(p, id, { password: 'nope' })).json().code).toBe('server.password_wrong');
    expect((await join(p, id, { password: 'friends-only' })).statusCode).toBe(200);
  });

  it('refuses offline servers, version mismatches, delisted servers and matchmaking maintenance', async () => {
    const offline = await createServer();
    const p = await registerPlayer(h);
    expect((await join(p, offline.id)).json().code).toBe('server.offline');

    const { id, token } = await createServer();
    await linked(token, { protocolVersion: 1 });
    const mismatch = await join(p, id, {}, { 'x-protocol': '2' });
    expect(mismatch.json()).toMatchObject({ code: 'server.version_mismatch', serverProtocol: 1 });

    await h.db.db.insert(maintenanceWindows).values({
      id: newId(),
      scope: 'matchmaking',
      startsAt: new Date(Date.now() - 60_000),
      message: { en: 'Matchmaking paused' },
    });
    h.app.liveops.invalidate();
    expect((await join(p, id)).json().code).toBe('platform.maintenance');
    await h.db.sql`UPDATE maintenance_windows SET cancelled_at = now()`;
    h.app.liveops.invalidate();

    const delist = await h.inject({
      method: 'PATCH',
      url: `/admin/v1/servers/${id}`,
      cookies: staffCookies(admin),
      headers: staffHeaders(admin),
      payload: { listing: 'delisted', reason: 'rule violation' },
    });
    expect(delist.statusCode).toBe(200);
    expect((await join(p, id)).json().code).toBe('server.not_found');
  });
});

describe('admin commands over the link', () => {
  it('relays a command and returns the server answer (audited)', async () => {
    const { id, token } = await createServer();
    await linked(token);
    const res = await h.inject({
      method: 'POST',
      url: `/admin/v1/servers/${id}/commands`,
      cookies: staffCookies(admin),
      headers: staffHeaders(admin),
      payload: { name: 'broadcast', args: { message: 'Restart in 10 minutes' }, reason: 'scheduled restart' },
    });
    expect(res.statusCode, res.body).toBe(200);
    expect(res.json()).toEqual({
      ok: true,
      result: { handled: 'broadcast', args: { message: 'Restart in 10 minutes' } },
    });
    const audit = await h.db.sql<
      { result: string }[]
    >`SELECT result FROM audit_log WHERE action = 'server.command.broadcast' AND target_id = ${id}`;
    expect(audit.map((a) => a.result)).toEqual(['ok']);
  });

  it('routes commands through Redis when the server is connected to a different API instance', async () => {
    const { id, token } = await createServer();
    await linked(token); // connected to instance A
    const peer = await h.peer(); // instance B shares DB/Redis
    const res = await peer.inject({
      method: 'POST',
      url: `/admin/v1/servers/${id}/commands`,
      cookies: staffCookies(admin),
      headers: staffHeaders(admin),
      payload: { name: 'save', args: {}, reason: 'pre-wipe backup' },
    });
    expect(res.statusCode, res.body).toBe(200);
    expect(res.json().result).toEqual({ handled: 'save', args: {} });
  });

  it('reports offline servers and timeouts', async () => {
    const offline = await createServer();
    const send = (serverId: string) =>
      h.inject({
        method: 'POST',
        url: `/admin/v1/servers/${serverId}/commands`,
        cookies: staffCookies(admin),
        headers: staffHeaders(admin),
        payload: { name: 'save', args: {}, reason: 'backup' },
      });
    expect((await send(offline.id)).json().code).toBe('server.offline');

    const { id, token } = await createServer();
    const silent = await linked(token);
    silent.commandHandler = () => null; // never answers
    const timeout = await send(id);
    expect(timeout.statusCode).toBe(504);
    expect(timeout.json().code).toBe('server.command_timeout');
  });

  it('requires servers.command permission', async () => {
    const { id } = await createServer();
    const analyst = await createStaff(h, ['analyst']);
    const res = await h.inject({
      method: 'POST',
      url: `/admin/v1/servers/${id}/commands`,
      cookies: staffCookies(analyst),
      headers: staffHeaders(analyst),
      payload: { name: 'save', args: {}, reason: 'x x x' },
    });
    expect(res.statusCode).toBe(403);
  });
});
