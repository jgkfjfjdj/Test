import type { FastifyPluginAsyncTypebox } from '@fastify/type-provider-typebox';
import {
  CommandResultPayload,
  ErrorCodes,
  HeartbeatPayload,
  HelloPayload,
  LinkCloseCodes,
  PlayerJoinedPayload,
  PlayerLeftPayload,
  TICKET_ISSUER,
  type LinkEnvelope,
} from '@kindling/contracts';
import { gameServers, newId, playSessions } from '@kindling/db';
import { and, eq, isNull } from 'drizzle-orm';
import type { FastifyRequest } from 'fastify';
import { Type, type Static, type TSchema } from 'typebox';
import { Value } from 'typebox/value';
import type { WebSocket } from 'ws';
import { ProblemError, pgError } from '../../lib/problem.js';
import { clearLive, liveKey, reserveKey, ticketKey, writeHeartbeat, writeHello } from './live.js';
import { ServerRegistry, type GameServerRow } from './registry.js';

declare module 'fastify' {
  interface FastifyRequest {
    gameServer?: GameServerRow;
  }
}

export const HEARTBEAT_INTERVAL_SECONDS = 10;
const HELLO_TIMEOUT_MS = 10_000;
const PING_INTERVAL_MS = 20_000;
const MAX_VIOLATIONS = 5;

function check<T extends TSchema>(schema: T, value: unknown): value is Static<T> {
  return Value.Check(schema, value);
}

export const serverLinkRoutes: FastifyPluginAsyncTypebox = async (app) => {
  const { ctx } = app;
  const registry = new ServerRegistry(ctx);

  app.get(
    '/jwks',
    {
      schema: {
        tags: ['server-link'],
        summary: 'Public keys for verifying join tickets (EdDSA / Ed25519).',
        response: { 200: Type.Object({ issuer: Type.String(), keys: Type.Array(Type.Unknown()) }) },
      },
    },
    async (_request, reply) => {
      reply.header('cache-control', 'public, max-age=300');
      return { issuer: TICKET_ISSUER, keys: ctx.keys.tickets.publicJwks().keys };
    },
  );

  const authenticate = async (request: FastifyRequest) => {
    const header = request.headers.authorization;
    const server = header?.startsWith('Bearer ') ? await registry.authenticate(header.slice(7).trim()) : null;
    if (!server) throw ProblemError.unauthorized(ErrorCodes.unauthorized, 'Invalid server token.');
    if (server.listing === 'delisted')
      throw ProblemError.forbidden(ErrorCodes.unauthorized, 'This server is delisted.');
    request.gameServer = server;
  };

  app.get(
    '/link',
    { websocket: true, preValidation: authenticate, schema: { hide: true } },
    (socket: WebSocket, request) => {
      const server = request.gameServer!;
      const serverId = server.id;
      const log = request.log.child({ serverId });
      let helloDone = false;
      let violations = 0;
      let lastSeenWrite = 0;

      const send = (envelope: LinkEnvelope) => {
        if (socket.readyState === socket.OPEN) socket.send(JSON.stringify(envelope));
      };
      const violation = (message: string, re?: string) => {
        violations += 1;
        send({ t: 'error', ...(re ? { re } : {}), d: { code: 'protocol_violation', message } });
        if (violations >= MAX_VIOLATIONS || !helloDone)
          socket.close(LinkCloseCodes.protocolViolation, message.slice(0, 120));
      };

      const helloTimer = setTimeout(() => {
        if (!helloDone) socket.close(LinkCloseCodes.helloTimeout, 'hello not received');
      }, HELLO_TIMEOUT_MS);
      let alive = true;
      const pinger = setInterval(() => {
        if (!alive) return socket.terminate();
        alive = false;
        socket.ping();
      }, PING_INTERVAL_MS);
      socket.on('pong', () => {
        alive = true;
      });

      const handlers: Record<string, (msg: LinkEnvelope) => Promise<void>> = {
        hello: async (msg) => {
          if (!check(HelloPayload, msg.d)) return violation('invalid hello', msg.id);
          const hello = msg.d;
          const now = ctx.now();
          await ctx.db
            .update(gameServers)
            .set({
              host: hello.host,
              gamePort: hello.gamePort,
              queryPort: hello.queryPort,
              maxPlayers: hello.maxPlayers,
              passwordHash: hello.passwordHash,
              mapId: hello.mapId,
              mode: hello.mode,
              tags: hello.tags,
              configSummary: hello.configSummary,
              buildVersion: hello.buildVersion,
              protocolVersion: hello.protocolVersion,
              lastWipeAt: hello.lastWipeAt ? new Date(hello.lastWipeAt) : null,
              nextWipeAt: hello.nextWipeAt ? new Date(hello.nextWipeAt) : null,
              lastSeenAt: now,
              updatedAt: now,
            })
            .where(eq(gameServers.id, serverId));
          await app.linkHub.attach(serverId, socket);
          await writeHello(ctx.redis, serverId, hello, now);
          helloDone = true;
          clearTimeout(helloTimer);
          log.info({ build: hello.buildVersion, protocol: hello.protocolVersion }, 'game server linked');
          send({
            t: 'welcome',
            ...(msg.id ? { re: msg.id } : {}),
            d: {
              serverId,
              ticketIssuer: TICKET_ISSUER,
              jwks: ctx.keys.tickets.publicJwks(),
              heartbeatIntervalSeconds: HEARTBEAT_INTERVAL_SECONDS,
            },
          });
        },

        heartbeat: async (msg) => {
          if (!check(HeartbeatPayload, msg.d)) return violation('invalid heartbeat');
          const now = ctx.now();
          await writeHeartbeat(ctx.redis, serverId, msg.d, now);
          if (now.getTime() - lastSeenWrite > 60_000) {
            lastSeenWrite = now.getTime();
            await ctx.db.update(gameServers).set({ lastSeenAt: now }).where(eq(gameServers.id, serverId));
          }
        },

        player_joined: async (msg) => {
          if (!check(PlayerJoinedPayload, msg.d)) return violation('invalid player_joined', msg.id);
          const { accountId, jti } = msg.d;
          // The ticket must have been issued by us for this account and server, and not used before.
          const issued = await ctx.redis.getdel(ticketKey(jti));
          let ok = issued === `${accountId}:${serverId}`;
          let reason: string | undefined = ok ? undefined : 'ticket_not_issued_or_already_used';
          if (ok) {
            try {
              await ctx.db
                .insert(playSessions)
                .values({ id: newId(), accountId, serverId, joinedAt: ctx.now() });
            } catch (err) {
              if (pgError(err).code !== '23503') throw err;
              ok = false;
              reason = 'unknown_account';
            }
          }
          if (ok) {
            await ctx.redis
              .multi()
              .zrem(reserveKey(serverId), accountId)
              .hincrby(liveKey(serverId), 'players', 1) // counted until the next heartbeat overwrites it
              .exec();
          } else {
            log.warn({ accountId, reason }, 'player join rejected by backend');
          }
          send({
            t: 'player_joined_ack',
            ...(msg.id ? { re: msg.id } : {}),
            d: { accountId, ok, ...(reason ? { reason } : {}) },
          });
        },

        player_left: async (msg) => {
          if (!check(PlayerLeftPayload, msg.d)) return violation('invalid player_left');
          await ctx.db
            .update(playSessions)
            .set({ leftAt: ctx.now(), leaveReason: msg.d.reason })
            .where(
              and(
                eq(playSessions.accountId, msg.d.accountId),
                eq(playSessions.serverId, serverId),
                isNull(playSessions.leftAt),
              ),
            );
        },

        cmd_result: async (msg) => {
          if (!msg.re || !check(CommandResultPayload, msg.d)) return violation('invalid cmd_result');
          await app.linkHub.publishResult(msg.re, msg.d);
        },
      };

      socket.on('message', (raw, isBinary) => {
        void (async () => {
          let msg: LinkEnvelope;
          try {
            if (isBinary) throw new Error('binary frames are not supported');
            msg = JSON.parse(raw.toString()) as LinkEnvelope;
            if (typeof msg !== 'object' || msg === null || typeof msg.t !== 'string')
              throw new Error('missing type');
          } catch {
            return violation('malformed message');
          }
          if (!helloDone && msg.t !== 'hello') return violation('hello must be the first message', msg.id);
          if (helloDone && msg.t === 'hello') return violation('duplicate hello', msg.id);
          if (helloDone && !app.linkHub.isAttached(serverId, socket)) return; // replaced; ignore stragglers
          const handler = handlers[msg.t];
          if (!handler) return violation(`unknown message type "${msg.t}"`, msg.id);
          try {
            await handler(msg);
          } catch (err) {
            log.error({ err, type: msg.t }, 'server link handler failed');
            send({
              t: 'error',
              ...(msg.id ? { re: msg.id } : {}),
              d: { code: 'internal_error', message: 'backend error' },
            });
          }
        })();
      });

      socket.on('close', () => {
        clearTimeout(helloTimer);
        clearInterval(pinger);
        void (async () => {
          if (!app.linkHub.isAttached(serverId, socket)) return; // a newer link owns the live state
          await app.linkHub.detach(serverId, socket);
          await clearLive(ctx.redis, serverId).catch(() => undefined);
          await ctx.db
            .update(gameServers)
            .set({ lastSeenAt: ctx.now() })
            .where(eq(gameServers.id, serverId))
            .catch(() => undefined);
          log.info('game server link closed');
        })();
      });
    },
  );
};
