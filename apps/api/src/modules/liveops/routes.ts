import type { FastifyPluginAsyncTypebox } from '@fastify/type-provider-typebox';
import { Type } from 'typebox';
import { rateLimit } from '../../lib/rate-limit.js';
import { clientInfo } from './service.js';

const Maintenance = Type.Object({
  id: Type.String(),
  scope: Type.Union([
    Type.Literal('full'),
    Type.Literal('login'),
    Type.Literal('matchmaking'),
    Type.Literal('store'),
  ]),
  startsAt: Type.String({ format: 'date-time' }),
  endsAt: Type.Union([Type.String({ format: 'date-time' }), Type.Null()]),
  message: Type.Record(Type.String(), Type.String()),
});

const BootstrapResponse = Type.Object({
  serverTime: Type.String({ format: 'date-time' }),
  version: Type.Object({
    status: Type.Union([
      Type.Literal('ok'),
      Type.Literal('update_available'),
      Type.Literal('update_required'),
    ]),
    latestBuild: Type.Union([Type.Integer(), Type.Null()]),
    minSupportedBuild: Type.Union([Type.Integer(), Type.Null()]),
    storeUrl: Type.Union([Type.String(), Type.Null()]),
  }),
  maintenance: Type.Object({ active: Type.Array(Maintenance), upcoming: Type.Array(Maintenance) }),
  config: Type.Object({ version: Type.String(), values: Type.Record(Type.String(), Type.Unknown()) }),
});

const ConfigResponse = Type.Object({
  version: Type.String(),
  values: Type.Record(Type.String(), Type.Unknown()),
});

export const liveOpsRoutes: FastifyPluginAsyncTypebox = async (app) => {
  const { config } = app.ctx;
  const liveops = app.liveops;

  app.get(
    '/bootstrap',
    {
      schema: {
        tags: ['bootstrap'],
        summary: 'First call at app start: version gate, maintenance windows and remote config.',
        description:
          'Send X-Platform (default ios), X-Build (CFBundleVersion), X-Protocol and X-Client-Version headers. ' +
          'Never requires sign-in and is not blocked by maintenance.',
        response: { 200: BootstrapResponse },
      },
      config: rateLimit(config, 'bootstrap', 120, '1 minute'),
    },
    async (request) => {
      const client = clientInfo(request);
      const [version, maintenance, remote] = await Promise.all([
        liveops.versionStatus(client),
        liveops.maintenance(),
        liveops.clientConfig(),
      ]);
      return { serverTime: app.ctx.now().toISOString(), version, maintenance, config: remote };
    },
  );

  app.get(
    '/config',
    {
      schema: {
        tags: ['bootstrap'],
        summary: 'Remote config snapshot. Supports If-None-Match with the returned ETag.',
        response: { 200: ConfigResponse, 304: Type.Null() },
      },
      config: rateLimit(config, 'config', 120, '1 minute'),
    },
    async (request, reply) => {
      const remote = await liveops.clientConfig();
      const etag = `"${remote.version}"`;
      reply.header('etag', etag);
      if (request.headers['if-none-match'] === etag) return reply.status(304).send(null);
      return remote;
    },
  );
};
