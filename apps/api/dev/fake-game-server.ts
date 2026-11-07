import type { HeartbeatPayload, HelloPayload, LinkEnvelope } from '@kindling/contracts';
import type { JSONWebKeySet } from 'jose';
import WebSocket from 'ws';
import { TicketVerifier, type VerifiedTicket } from '../src/modules/servers/ticket-verifier.js';

export const DEFAULT_HELLO: HelloPayload = {
  buildVersion: '0.2.0+test',
  protocolVersion: 1,
  host: 'eu1.kindling.example',
  gamePort: 27015,
  queryPort: 27016,
  maxPlayers: 50,
  passwordHash: null,
  mapId: 'map.reach_2k',
  mode: 'pvp',
  tags: ['official', 'biweekly'],
  configSummary: { gather: 1, sleepers: 'protected-in-claim' },
  lastWipeAt: null,
  nextWipeAt: null,
};

type CommandHandler = (
  name: string,
  args: Record<string, unknown>,
) => { ok: boolean; result?: unknown; error?: string } | null;

/**
 * Minimal stand-in for the Unity dedicated server: speaks the real server-link protocol over a
 * real WebSocket and verifies join tickets with the reference verifier.
 */
export class FakeGameServer {
  readonly received: LinkEnvelope[] = [];
  serverId = '';
  verifier?: TicketVerifier;
  closed?: { code: number; reason: string };
  private seq = 0;
  private listeners: ((msg: LinkEnvelope) => void)[] = [];
  private closeListeners: (() => void)[] = [];
  commandHandler: CommandHandler = (name, args) => ({ ok: true, result: { handled: name, args } });

  private constructor(readonly ws: WebSocket) {
    ws.on('message', (raw) => {
      const msg = JSON.parse(raw.toString()) as LinkEnvelope;
      this.received.push(msg);
      if (msg.t === 'cmd' && msg.id) {
        const d = msg.d as { name: string; args: Record<string, unknown> };
        const answer = this.commandHandler(d.name, d.args);
        if (answer) this.send({ t: 'cmd_result', re: msg.id, d: answer });
      }
      for (const l of this.listeners) l(msg);
    });
    ws.on('close', (code, reason) => {
      this.closed = { code, reason: reason.toString() };
      for (const l of this.closeListeners) l();
    });
  }

  /** Opens the link. Rejects with the HTTP status if the upgrade is refused. */
  static open(baseUrl: string, token: string): Promise<FakeGameServer> {
    return new Promise((resolve, reject) => {
      const ws = new WebSocket(`${baseUrl.replace(/^http/, 'ws')}/server/v1/link`, {
        headers: { authorization: `Bearer ${token}` },
      });
      const server = new FakeGameServer(ws);
      ws.once('open', () => resolve(server));
      ws.once('unexpected-response', (_req, res) => reject(new Error(`HTTP ${res.statusCode}`)));
      ws.once('error', reject);
    });
  }

  static async connect(
    baseUrl: string,
    token: string,
    hello: Partial<HelloPayload> = {},
    now: () => number = () => Date.now(),
  ): Promise<FakeGameServer> {
    const server = await FakeGameServer.open(baseUrl, token);
    const welcome = await server.request('hello', { ...DEFAULT_HELLO, ...hello }, 'welcome');
    const d = welcome.d as { serverId: string; jwks: JSONWebKeySet };
    server.serverId = d.serverId;
    server.verifier = new TicketVerifier(d.serverId, d.jwks, now);
    return server;
  }

  send(msg: LinkEnvelope): void {
    this.ws.send(JSON.stringify(msg));
  }

  waitFor(predicate: (msg: LinkEnvelope) => boolean, timeoutMs = 3000): Promise<LinkEnvelope> {
    const existing = this.received.find(predicate);
    if (existing) return Promise.resolve(existing);
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('timed out waiting for message')), timeoutMs);
      this.listeners.push((msg) => {
        if (predicate(msg)) {
          clearTimeout(timer);
          resolve(msg);
        }
      });
    });
  }

  waitForClose(timeoutMs = 3000): Promise<{ code: number; reason: string }> {
    if (this.closed) return Promise.resolve(this.closed);
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('timed out waiting for close')), timeoutMs);
      this.closeListeners.push(() => {
        clearTimeout(timer);
        resolve(this.closed!);
      });
    });
  }

  /** Sends a request and waits for the reply of the given type (correlated by id). */
  async request(t: string, d: unknown, replyType: string): Promise<LinkEnvelope> {
    const id = `m${++this.seq}`;
    const reply = this.waitFor((m) => m.re === id && (m.t === replyType || m.t === 'error'));
    this.send({ t, id, d });
    const msg = await reply;
    if (msg.t === 'error') throw new Error(`link error: ${JSON.stringify(msg.d)}`);
    return msg;
  }

  async heartbeat(hb: Partial<HeartbeatPayload> = {}): Promise<void> {
    this.send({
      t: 'heartbeat',
      d: {
        players: 0,
        queue: 0,
        tickMsAvg: 8,
        tickMsP99: 15,
        fps: 30,
        memoryMb: 2048,
        entities: 1000,
        uptimeSeconds: 60,
        ...hb,
      },
    });
    // Give the backend a moment to write the heartbeat (no reply is sent for heartbeats).
    await new Promise((r) => setTimeout(r, 50));
  }

  /** What the real server does when a client connects: verify offline, then report to the backend. */
  async admit(ticket: string): Promise<{ ticket: VerifiedTicket; ack: { ok: boolean; reason?: string } }> {
    const verified = await this.verifier!.verify(ticket);
    const ack = await this.request(
      'player_joined',
      { accountId: verified.accountId, jti: verified.jti },
      'player_joined_ack',
    );
    return { ticket: verified, ack: ack.d as { ok: boolean; reason?: string } };
  }

  close(): void {
    this.ws.close(1000, 'test done');
  }
}
