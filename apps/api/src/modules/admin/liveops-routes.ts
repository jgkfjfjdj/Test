import type { FastifyPluginAsyncTypebox } from '@fastify/type-provider-typebox';
import { maintenanceWindows, newId, remoteConfig, versionPolicy } from '@kindling/db';
import { and, desc, eq, isNull } from 'drizzle-orm';
import { Type } from 'typebox';
import { ProblemError } from '../../lib/problem.js';
import { requireStaff, staffOf } from '../../plugins/staff-auth.js';
import { AuditService } from '../audit/service.js';

const Reason = Type.String({ minLength: 3, maxLength: 500, description: 'Stored in the audit log.' });
const Scope = Type.Union([
  Type.Literal('full'),
  Type.Literal('login'),
  Type.Literal('matchmaking'),
  Type.Literal('store'),
]);

const PolicyView = Type.Object({
  platform: Type.String(),
  minSupportedBuild: Type.Integer(),
  latestBuild: Type.Integer(),
  storeUrl: Type.String(),
  updatedAt: Type.String({ format: 'date-time' }),
});

const MaintenanceView = Type.Object({
  id: Type.String(),
  scope: Scope,
  startsAt: Type.String({ format: 'date-time' }),
  endsAt: Type.Union([Type.String({ format: 'date-time' }), Type.Null()]),
  message: Type.Record(Type.String(), Type.String()),
  cancelledAt: Type.Union([Type.String({ format: 'date-time' }), Type.Null()]),
});

const ConfigEntry = Type.Object({
  key: Type.String(),
  value: Type.Unknown(),
  scope: Type.Union([Type.Literal('client'), Type.Literal('server'), Type.Literal('both')]),
  description: Type.Union([Type.String(), Type.Null()]),
  version: Type.Integer(),
  updatedAt: Type.String({ format: 'date-time' }),
});

type MaintenanceRow = typeof maintenanceWindows.$inferSelect;
const maintenanceView = (r: MaintenanceRow) => ({
  id: r.id,
  scope: r.scope,
  startsAt: r.startsAt.toISOString(),
  endsAt: r.endsAt?.toISOString() ?? null,
  message: r.message,
  cancelledAt: r.cancelledAt?.toISOString() ?? null,
});

type ConfigRow = typeof remoteConfig.$inferSelect;
const configView = (r: ConfigRow) => ({
  key: r.key,
  value: r.value,
  scope: r.scope,
  description: r.description ?? null,
  version: r.version,
  updatedAt: r.updatedAt.toISOString(),
});

/** Staff endpoints that drive what /v1/bootstrap reports. Every write is audited and needs a reason. */
export const adminLiveOpsRoutes: FastifyPluginAsyncTypebox = async (app) => {
  const { ctx } = app;
  const audit = new AuditService(ctx);

  // ------------------------------------------------------------ version policy

  app.get(
    '/version-policy',
    {
      schema: {
        tags: ['admin'],
        summary: 'Version policy per platform.',
        security: [{ staffSession: [] }],
        response: { 200: Type.Object({ items: Type.Array(PolicyView) }) },
      },
      preHandler: requireStaff('versions.manage'),
    },
    async () => {
      const rows = await ctx.db.select().from(versionPolicy).orderBy(versionPolicy.platform);
      return { items: rows.map((r) => ({ ...r, updatedAt: r.updatedAt.toISOString() })) };
    },
  );

  app.put(
    '/version-policy/:platform',
    {
      schema: {
        tags: ['admin'],
        summary: 'Set minimum supported and latest build (builds below the minimum must update).',
        security: [{ staffSession: [] }],
        params: Type.Object({ platform: Type.String({ pattern: '^[a-z]{2,16}$' }) }),
        body: Type.Object(
          {
            minSupportedBuild: Type.Integer({ minimum: 0 }),
            latestBuild: Type.Integer({ minimum: 0 }),
            storeUrl: Type.String({ pattern: '^https://', maxLength: 300 }),
            reason: Reason,
          },
          { additionalProperties: false },
        ),
        response: { 200: PolicyView },
      },
      preHandler: requireStaff('versions.manage'),
    },
    async (request) => {
      const { platform } = request.params;
      const { reason, ...values } = request.body;
      if (values.minSupportedBuild > values.latestBuild) {
        throw ProblemError.badRequest(
          'common.validation_failed',
          'minSupportedBuild cannot exceed latestBuild.',
        );
      }
      const row = await ctx.db.transaction(async (tx) => {
        const [before] = await tx
          .select()
          .from(versionPolicy)
          .where(eq(versionPolicy.platform, platform))
          .limit(1);
        const [after] = await tx
          .insert(versionPolicy)
          .values({ platform, ...values, updatedBy: staffOf(request).id, updatedAt: ctx.now() })
          .onConflictDoUpdate({
            target: versionPolicy.platform,
            set: { ...values, updatedBy: staffOf(request).id, updatedAt: ctx.now() },
          })
          .returning();
        await audit.recordStaff(
          request,
          {
            action: 'version_policy.update',
            targetType: 'version_policy',
            targetId: platform,
            reason,
            before: before ?? null,
            after: values,
          },
          tx,
        );
        return after!;
      });
      app.liveops.invalidate();
      return { ...row, updatedAt: row.updatedAt.toISOString() };
    },
  );

  // ------------------------------------------------------------ maintenance

  app.get(
    '/maintenance',
    {
      schema: {
        tags: ['admin'],
        summary: 'Recent maintenance windows (newest first).',
        security: [{ staffSession: [] }],
        response: { 200: Type.Object({ items: Type.Array(MaintenanceView) }) },
      },
      preHandler: requireStaff('liveops.maintenance'),
    },
    async () => {
      const rows = await ctx.db
        .select()
        .from(maintenanceWindows)
        .orderBy(desc(maintenanceWindows.startsAt))
        .limit(100);
      return { items: rows.map(maintenanceView) };
    },
  );

  app.post(
    '/maintenance',
    {
      schema: {
        tags: ['admin'],
        summary: 'Schedule maintenance (omit startsAt to start immediately).',
        security: [{ staffSession: [] }],
        body: Type.Object(
          {
            scope: Scope,
            startsAt: Type.Optional(Type.String({ maxLength: 40 })),
            endsAt: Type.Optional(Type.String({ maxLength: 40 })),
            message: Type.Record(
              Type.String({ pattern: '^[a-z]{2}(-[A-Z]{2})?$' }),
              Type.String({ minLength: 1, maxLength: 500 }),
            ),
            reason: Reason,
          },
          { additionalProperties: false },
        ),
        response: { 201: MaintenanceView },
      },
      preHandler: requireStaff('liveops.maintenance'),
    },
    async (request, reply) => {
      const { scope, message, reason } = request.body;
      if (!message['en'])
        throw ProblemError.badRequest('common.validation_failed', 'An English ("en") message is required.');
      const startsAt = request.body.startsAt ? new Date(request.body.startsAt) : ctx.now();
      const endsAt = request.body.endsAt ? new Date(request.body.endsAt) : null;
      if (
        Number.isNaN(startsAt.getTime()) ||
        (endsAt && (Number.isNaN(endsAt.getTime()) || endsAt <= startsAt))
      ) {
        throw ProblemError.badRequest(
          'common.validation_failed',
          'startsAt/endsAt must be valid ISO dates with endsAt after startsAt.',
        );
      }
      const row = await ctx.db.transaction(async (tx) => {
        const [created] = await tx
          .insert(maintenanceWindows)
          .values({ id: newId(), scope, startsAt, endsAt, message, createdBy: staffOf(request).id })
          .returning();
        await audit.recordStaff(
          request,
          {
            action: 'maintenance.create',
            targetType: 'maintenance',
            targetId: created!.id,
            reason,
            after: maintenanceView(created!),
          },
          tx,
        );
        return created!;
      });
      app.liveops.invalidate();
      return reply.status(201).send(maintenanceView(row));
    },
  );

  app.post(
    '/maintenance/:id/cancel',
    {
      schema: {
        tags: ['admin'],
        summary: 'Cancel (or end early) a maintenance window.',
        security: [{ staffSession: [] }],
        params: Type.Object({ id: Type.String({ pattern: '^[0-9a-fA-F-]{36}$' }) }),
        body: Type.Object({ reason: Reason }, { additionalProperties: false }),
        response: { 200: MaintenanceView },
      },
      preHandler: requireStaff('liveops.maintenance'),
    },
    async (request) => {
      const row = await ctx.db.transaction(async (tx) => {
        const [updated] = await tx
          .update(maintenanceWindows)
          .set({ cancelledAt: ctx.now() })
          .where(and(eq(maintenanceWindows.id, request.params.id), isNull(maintenanceWindows.cancelledAt)))
          .returning();
        if (!updated) throw ProblemError.notFound('No active maintenance window with this id.');
        await audit.recordStaff(
          request,
          {
            action: 'maintenance.cancel',
            targetType: 'maintenance',
            targetId: updated.id,
            reason: request.body.reason,
          },
          tx,
        );
        return updated;
      });
      app.liveops.invalidate();
      return maintenanceView(row);
    },
  );

  // ------------------------------------------------------------ remote config

  app.get(
    '/config',
    {
      schema: {
        tags: ['admin'],
        summary: 'All remote config entries.',
        security: [{ staffSession: [] }],
        response: { 200: Type.Object({ items: Type.Array(ConfigEntry) }) },
      },
      preHandler: requireStaff('liveops.config'),
    },
    async () => ({
      items: (await ctx.db.select().from(remoteConfig).orderBy(remoteConfig.key)).map(configView),
    }),
  );

  app.put(
    '/config/:key',
    {
      schema: {
        tags: ['admin'],
        summary: 'Create or update a remote config entry (version increments on every change).',
        security: [{ staffSession: [] }],
        params: Type.Object({
          key: Type.String({ pattern: '^[a-z][a-zA-Z0-9]*(\\.[a-z][a-zA-Z0-9]*)+$', maxLength: 100 }),
        }),
        body: Type.Object(
          {
            value: Type.Unknown(),
            scope: Type.Union([Type.Literal('client'), Type.Literal('server'), Type.Literal('both')]),
            description: Type.Optional(Type.String({ maxLength: 500 })),
            reason: Reason,
          },
          { additionalProperties: false },
        ),
        response: { 200: ConfigEntry },
      },
      preHandler: requireStaff('liveops.config'),
    },
    async (request) => {
      const { key } = request.params;
      const { value, scope, description, reason } = request.body;
      if (value === undefined)
        throw ProblemError.badRequest('common.validation_failed', 'value is required (use null explicitly).');
      if (Buffer.byteLength(JSON.stringify(value)) > 16 * 1024) {
        throw ProblemError.badRequest('common.validation_failed', 'Config values are limited to 16 KB.');
      }
      const row = await ctx.db.transaction(async (tx) => {
        const [before] = await tx.select().from(remoteConfig).where(eq(remoteConfig.key, key)).limit(1);
        const values = {
          value,
          scope,
          description: description ?? before?.description ?? null,
          updatedBy: staffOf(request).id,
          updatedAt: ctx.now(),
        };
        const [after] = before
          ? await tx
              .update(remoteConfig)
              .set({ ...values, version: before.version + 1 })
              .where(eq(remoteConfig.key, key))
              .returning()
          : await tx
              .insert(remoteConfig)
              .values({ key, ...values, version: 1 })
              .returning();
        await audit.recordStaff(
          request,
          {
            action: 'config.update',
            targetType: 'remote_config',
            targetId: key,
            reason,
            before: before ? { value: before.value, scope: before.scope } : null,
            after: { value, scope },
          },
          tx,
        );
        return after!;
      });
      app.liveops.invalidate();
      return configView(row);
    },
  );

  app.delete(
    '/config/:key',
    {
      schema: {
        tags: ['admin'],
        summary: 'Delete a remote config entry (clients fall back to built-in defaults).',
        security: [{ staffSession: [] }],
        params: Type.Object({ key: Type.String({ maxLength: 100 }) }),
        body: Type.Object({ reason: Reason }, { additionalProperties: false }),
        response: { 204: Type.Null() },
      },
      preHandler: requireStaff('liveops.config'),
    },
    async (request, reply) => {
      await ctx.db.transaction(async (tx) => {
        const [before] = await tx
          .delete(remoteConfig)
          .where(eq(remoteConfig.key, request.params.key))
          .returning();
        if (!before) throw ProblemError.notFound('No config entry with this key.');
        await audit.recordStaff(
          request,
          {
            action: 'config.delete',
            targetType: 'remote_config',
            targetId: before.key,
            reason: request.body.reason,
            before: { value: before.value, scope: before.scope },
          },
          tx,
        );
      });
      app.liveops.invalidate();
      return reply.status(204).send(null);
    },
  );
};
