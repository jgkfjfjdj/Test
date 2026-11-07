import type { FastifyPluginAsyncTypebox } from '@fastify/type-provider-typebox';
import { ErrorCodes, LinkCloseCodes, SERVER_COMMANDS } from '@kindling/contracts';
import { gameServers } from '@kindling/db';
import { desc, eq } from 'drizzle-orm';
import { Type } from 'typebox';
import { ProblemError } from '../../lib/problem.js';
import { requireStaff } from '../../plugins/staff-auth.js';
import { AuditService } from '../audit/service.js';
import { readLive } from './live.js';
import { ServerRegistry } from './registry.js';

const Reason = Type.String({ minLength: 3, maxLength: 500, description: 'Stored in the audit log.' });
const IdParams = Type.Object({ id: Type.String({ pattern: '^[0-9a-fA-F-]{36}$' }) });
const Listing = Type.Union([
  Type.Literal('unlisted'),
  Type.Literal('listed'),
  Type.Literal('verified'),
  Type.Literal('delisted'),
]);

const AdminServer = Type.Object({
  id: Type.String(),
  kind: Type.String(),
  name: Type.String(),
  region: Type.String(),
  listing: Type.String(),
  mode: Type.String(),
  maxPlayers: Type.Integer(),
  host: Type.Union([Type.String(), Type.Null()]),
  buildVersion: Type.Union([Type.String(), Type.Null()]),
  protocolVersion: Type.Union([Type.Integer(), Type.Null()]),
  lastSeenAt: Type.Union([Type.String({ format: 'date-time' }), Type.Null()]),
  online: Type.Boolean(),
  live: Type.Union([Type.Record(Type.String(), Type.Unknown()), Type.Null()]),
});

type Row = typeof gameServers.$inferSelect;

export const adminServerRoutes: FastifyPluginAsyncTypebox = async (app) => {
  const { ctx } = app;
  const registry = new ServerRegistry(ctx);
  const audit = new AuditService(ctx);

  const view = async (row: Row) => {
    const live = (await readLive(ctx.redis, [row.id])).get(row.id) ?? null;
    return {
      id: row.id,
      kind: row.kind,
      name: row.name,
      region: row.region,
      listing: row.listing,
      mode: row.mode,
      maxPlayers: row.maxPlayers,
      host: row.host,
      buildVersion: row.buildVersion,
      protocolVersion: row.protocolVersion,
      lastSeenAt: row.lastSeenAt?.toISOString() ?? null,
      online: live !== null,
      live: live as Record<string, unknown> | null,
    };
  };

  app.get(
    '/servers',
    {
      schema: {
        tags: ['admin'],
        summary: 'All registered servers with live state.',
        security: [{ staffSession: [] }],
        response: { 200: Type.Object({ items: Type.Array(AdminServer) }) },
      },
      preHandler: requireStaff('servers.read'),
    },
    async () => {
      const rows = await ctx.db.select().from(gameServers).orderBy(desc(gameServers.createdAt)).limit(500);
      return { items: await Promise.all(rows.map(view)) };
    },
  );

  app.post(
    '/servers',
    {
      schema: {
        tags: ['admin'],
        summary:
          'Register an official server. The server token is returned once — put it in the server config.',
        security: [{ staffSession: [] }],
        body: Type.Object(
          {
            name: Type.String({ minLength: 3, maxLength: 64 }),
            region: Type.String({ pattern: '^[a-z]{2,8}(-[a-z0-9]{2,12})*$', maxLength: 32 }),
            maxPlayers: Type.Integer({ minimum: 1, maximum: 200 }),
            mode: Type.Union([Type.Literal('pvp'), Type.Literal('pve')]),
            tags: Type.Array(Type.String({ minLength: 1, maxLength: 24 }), { maxItems: 12 }),
            listing: Type.Optional(Listing),
            reason: Reason,
          },
          { additionalProperties: false },
        ),
        response: { 201: Type.Object({ server: AdminServer, token: Type.String() }) },
      },
      preHandler: requireStaff('servers.manage'),
    },
    async (request, reply) => {
      const { reason, listing, ...input } = request.body;
      const { server, token } = await registry.createOfficial({ ...input, listing: listing ?? 'listed' });
      await audit.recordStaff(request, {
        action: 'server.create',
        targetType: 'server',
        targetId: server.id,
        reason,
        after: { ...input, listing: server.listing },
      });
      return reply.status(201).send({ server: await view(server), token });
    },
  );

  app.patch(
    '/servers/:id',
    {
      schema: {
        tags: ['admin'],
        summary: 'Rename, move region, or change listing (delist / verify).',
        security: [{ staffSession: [] }],
        params: IdParams,
        body: Type.Object(
          {
            name: Type.Optional(Type.String({ minLength: 3, maxLength: 64 })),
            region: Type.Optional(Type.String({ pattern: '^[a-z]{2,8}(-[a-z0-9]{2,12})*$', maxLength: 32 })),
            listing: Type.Optional(Listing),
            reason: Reason,
          },
          { additionalProperties: false },
        ),
        response: { 200: AdminServer },
      },
      preHandler: requireStaff('servers.manage'),
    },
    async (request) => {
      const { reason, ...changes } = request.body;
      const before = await registry.get(request.params.id);
      if (!before) throw ProblemError.notFound('Server not found.', ErrorCodes.serverNotFound);
      const [after] = await ctx.db.transaction(async (tx) => {
        const rows = await tx
          .update(gameServers)
          .set({
            ...changes,
            ...(changes.listing === 'delisted' ? { delistReason: reason } : {}),
            updatedAt: ctx.now(),
          })
          .where(eq(gameServers.id, before.id))
          .returning();
        await audit.recordStaff(
          request,
          {
            action: 'server.update',
            targetType: 'server',
            targetId: before.id,
            reason,
            before: { name: before.name, region: before.region, listing: before.listing },
            after: changes,
          },
          tx,
        );
        return rows;
      });
      if (changes.listing === 'delisted')
        await app.linkHub.disconnect(before.id, LinkCloseCodes.tokenRevoked, 'server delisted');
      return view(after!);
    },
  );

  app.post(
    '/servers/:id/token/rotate',
    {
      schema: {
        tags: ['admin'],
        summary: 'Revoke the server token and issue a new one (the current link is disconnected).',
        security: [{ staffSession: [] }],
        params: IdParams,
        body: Type.Object({ reason: Reason }, { additionalProperties: false }),
        response: { 200: Type.Object({ token: Type.String() }) },
      },
      preHandler: requireStaff('servers.manage'),
    },
    async (request) => {
      const token = await registry.rotateToken(request.params.id);
      await audit.recordStaff(request, {
        action: 'server.token_rotate',
        targetType: 'server',
        targetId: request.params.id,
        reason: request.body.reason,
      });
      await app.linkHub.disconnect(request.params.id, LinkCloseCodes.tokenRevoked, 'server token rotated');
      return { token };
    },
  );

  app.post(
    '/servers/:id/commands',
    {
      schema: {
        tags: ['admin'],
        summary: 'Send a command to a connected server and wait for its answer (10 s timeout).',
        security: [{ staffSession: [] }],
        params: IdParams,
        body: Type.Object(
          {
            name: Type.Union(SERVER_COMMANDS.map((c) => Type.Literal(c))),
            args: Type.Record(Type.String(), Type.Unknown()),
            reason: Reason,
          },
          { additionalProperties: false },
        ),
        response: {
          200: Type.Object({
            ok: Type.Boolean(),
            result: Type.Optional(Type.Unknown()),
            error: Type.Optional(Type.String()),
          }),
        },
      },
      preHandler: requireStaff('servers.command'),
    },
    async (request) => {
      const { name, args, reason } = request.body;
      const server = await registry.get(request.params.id);
      if (!server) throw ProblemError.notFound('Server not found.', ErrorCodes.serverNotFound);
      let outcome;
      try {
        outcome = await app.linkHub.sendCommand(
          server.id,
          { name, args },
          ctx.config.SERVER_COMMAND_TIMEOUT_MS,
        );
      } catch (err) {
        await audit.recordStaff(request, {
          action: `server.command.${name}`,
          targetType: 'server',
          targetId: server.id,
          reason,
          after: { args },
          result: 'failed',
        });
        throw err;
      }
      await audit.recordStaff(request, {
        action: `server.command.${name}`,
        targetType: 'server',
        targetId: server.id,
        reason,
        after: { args, response: outcome },
        result: outcome.ok ? 'ok' : 'rejected',
      });
      return outcome;
    },
  );
};
