import type { FastifyPluginAsyncTypebox } from '@fastify/type-provider-typebox';
import { Type } from 'typebox';
import { requireStaff } from '../../plugins/staff-auth.js';
import { AuditService } from './service.js';

const AuditItem = Type.Object({
  id: Type.String(),
  occurredAt: Type.String({ format: 'date-time' }),
  actorType: Type.String(),
  actorId: Type.Union([Type.String(), Type.Null()]),
  action: Type.String(),
  targetType: Type.Union([Type.String(), Type.Null()]),
  targetId: Type.Union([Type.String(), Type.Null()]),
  reason: Type.Union([Type.String(), Type.Null()]),
  before: Type.Unknown(),
  after: Type.Unknown(),
  result: Type.String(),
  requestId: Type.Union([Type.String(), Type.Null()]),
});

export const auditRoutes: FastifyPluginAsyncTypebox = async (app) => {
  const audit = new AuditService(app.ctx);
  app.get(
    '/audit',
    {
      schema: {
        tags: ['admin'],
        summary: 'Audit log, newest first, with filters and cursor pagination.',
        security: [{ staffSession: [] }],
        querystring: Type.Object(
          {
            actorId: Type.Optional(Type.String({ maxLength: 36 })),
            action: Type.Optional(Type.String({ maxLength: 100 })),
            targetType: Type.Optional(Type.String({ maxLength: 50 })),
            targetId: Type.Optional(Type.String({ maxLength: 100 })),
            cursor: Type.Optional(Type.String({ maxLength: 200 })),
            limit: Type.Optional(Type.Integer({ minimum: 1, maximum: 100 })),
          },
          { additionalProperties: false },
        ),
        response: {
          200: Type.Object({
            items: Type.Array(AuditItem),
            nextCursor: Type.Union([Type.String(), Type.Null()]),
          }),
        },
      },
      preHandler: requireStaff('audit.read'),
    },
    async (request) => audit.list({ ...request.query, limit: request.query.limit ?? 50 }),
  );
};
