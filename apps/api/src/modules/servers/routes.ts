import type { FastifyPluginAsyncTypebox } from '@fastify/type-provider-typebox';
import { Type } from 'typebox';
import { rateLimit } from '../../lib/rate-limit.js';
import { playerOf, requirePlayer } from '../../plugins/player-auth.js';
import { clientInfo } from '../liveops/service.js';
import { ServerBrowser } from './browser.js';

const Nullable = <T extends Parameters<typeof Type.Union>[0][number]>(schema: T) =>
  Type.Union([schema, Type.Null()]);

const ServerItem = Type.Object({
  id: Type.String(),
  name: Type.String(),
  region: Type.String(),
  kind: Type.String(),
  verified: Type.Boolean(),
  mode: Type.String(),
  tags: Type.Array(Type.String()),
  mapId: Nullable(Type.String()),
  players: Type.Integer(),
  maxPlayers: Type.Integer(),
  queue: Type.Integer(),
  passwordProtected: Type.Boolean(),
  protocolVersion: Type.Integer(),
  compatible: Type.Boolean(),
  host: Nullable(Type.String()),
  gamePort: Nullable(Type.Integer()),
  queryPort: Nullable(Type.Integer()),
  lastWipeAt: Nullable(Type.String({ format: 'date-time' })),
  nextWipeAt: Nullable(Type.String({ format: 'date-time' })),
});

const IdParams = Type.Object({ id: Type.String({ pattern: '^[0-9a-fA-F-]{36}$' }) });

export const serverBrowserRoutes: FastifyPluginAsyncTypebox = async (app) => {
  const { config } = app.ctx;
  const browser = new ServerBrowser(app.ctx);

  app.get(
    '',
    {
      schema: {
        tags: ['servers'],
        summary: 'Server browser: online, listed servers. Send X-Protocol to hide incompatible servers.',
        security: [{ bearer: [] }],
        querystring: Type.Object(
          {
            region: Type.Optional(Type.String({ maxLength: 32 })),
            mode: Type.Optional(Type.Union([Type.Literal('pvp'), Type.Literal('pve')])),
            official: Type.Optional(Type.Boolean()),
            notFull: Type.Optional(Type.Boolean()),
            hasPlayers: Type.Optional(Type.Boolean()),
            noPassword: Type.Optional(Type.Boolean()),
            search: Type.Optional(Type.String({ maxLength: 64 })),
            includeIncompatible: Type.Optional(Type.Boolean()),
            cursor: Type.Optional(Type.String({ maxLength: 32 })),
            limit: Type.Optional(Type.Integer({ minimum: 1, maximum: 100 })),
          },
          { additionalProperties: false },
        ),
        response: {
          200: Type.Object({
            items: Type.Array(ServerItem),
            nextCursor: Nullable(Type.String()),
            total: Type.Integer(),
          }),
        },
      },
      preHandler: requirePlayer,
      config: rateLimit(
        config,
        'browse',
        120,
        '1 minute',
        (request) => request.player?.accountId ?? request.ip,
      ),
    },
    async (request) =>
      browser.list({ ...request.query, limit: request.query.limit ?? 50 }, clientInfo(request).protocol),
  );

  app.get(
    '/:id',
    {
      schema: {
        tags: ['servers'],
        summary: 'Server details (rules summary, live population).',
        security: [{ bearer: [] }],
        params: IdParams,
        response: {
          200: Type.Object({
            id: Type.String(),
            name: Type.String(),
            description: Type.String(),
            region: Type.String(),
            kind: Type.String(),
            verified: Type.Boolean(),
            mode: Type.String(),
            tags: Type.Array(Type.String()),
            mapId: Nullable(Type.String()),
            configSummary: Type.Record(Type.String(), Type.Unknown()),
            passwordProtected: Type.Boolean(),
            online: Type.Boolean(),
            live: Nullable(ServerItem),
          }),
        },
      },
      preHandler: requirePlayer,
    },
    async (request) => browser.detail(request.params.id, clientInfo(request).protocol),
  );

  app.post(
    '/:id/join',
    {
      schema: {
        tags: ['servers'],
        summary: 'Reserve a slot (60 s) and get a single-use join ticket to present to the game server.',
        security: [{ bearer: [] }],
        params: IdParams,
        body: Type.Object(
          { password: Type.Optional(Type.String({ maxLength: 128 })) },
          { additionalProperties: false },
        ),
        response: {
          200: Type.Object({
            ticket: Type.String(),
            expiresAt: Type.String({ format: 'date-time' }),
            server: Type.Object({
              id: Type.String(),
              host: Nullable(Type.String()),
              gamePort: Nullable(Type.Integer()),
              protocolVersion: Type.Integer(),
            }),
            reservation: Type.Object({ expiresInSeconds: Type.Integer(), reservedSlots: Type.Integer() }),
          }),
        },
      },
      preHandler: [requirePlayer, async () => app.liveops.assertAvailable('matchmaking')],
      config: rateLimit(config, 'join', 20, '1 minute', (request) => request.player?.accountId ?? request.ip),
    },
    async (request) =>
      browser.join(playerOf(request).accountId, request.params.id, {
        password: request.body?.password,
        protocol: clientInfo(request).protocol,
      }),
  );
};
