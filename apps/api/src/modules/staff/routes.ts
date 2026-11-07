import type { FastifyPluginAsyncTypebox } from '@fastify/type-provider-typebox';
import { ErrorCodes, PERMISSIONS } from '@kindling/contracts';
import { Type } from 'typebox';
import { ProblemError } from '../../lib/problem.js';
import { rateLimit } from '../../lib/rate-limit.js';
import { requireStaff, staffOf } from '../../plugins/staff-auth.js';
import { AuditService } from '../audit/service.js';
import { csrfFor, STAFF_COOKIE, STEP_UP_WINDOW_MS, StaffService } from './service.js';

const StaffMe = Type.Object({
  id: Type.String(),
  email: Type.String(),
  name: Type.String(),
  roles: Type.Array(Type.String()),
  permissions: Type.Array(Type.String()),
  csrfToken: Type.String(),
  stepUpValidUntil: Type.Union([Type.String({ format: 'date-time' }), Type.Null()]),
});

const StaffView = Type.Object({
  id: Type.String(),
  email: Type.String(),
  name: Type.String(),
  status: Type.String(),
  roles: Type.Array(Type.String()),
  lastLoginAt: Type.Union([Type.String({ format: 'date-time' }), Type.Null()]),
  createdAt: Type.String({ format: 'date-time' }),
});

const Reason = Type.String({
  minLength: 3,
  maxLength: 500,
  description: 'Why this change is made (stored in the audit log).',
});
const IdParams = Type.Object({ id: Type.String({ pattern: '^[0-9a-fA-F-]{36}$' }) });

export const staffRoutes: FastifyPluginAsyncTypebox = async (app) => {
  const { ctx } = app;
  const service = new StaffService(ctx);
  const audit = new AuditService(ctx);

  const cookieOptions = (expires: Date) => ({
    path: '/admin',
    httpOnly: true,
    secure: ctx.config.ADMIN_COOKIE_SECURE,
    sameSite: 'strict' as const,
    expires,
  });

  // ------------------------------------------------------------ setup (invited users)

  app.post(
    '/auth/setup/begin',
    {
      schema: {
        tags: ['admin'],
        summary: 'Invited staff: get a TOTP secret for the authenticator app.',
        body: Type.Object(
          { token: Type.String({ minLength: 20, maxLength: 128 }) },
          { additionalProperties: false },
        ),
        response: {
          200: Type.Object({ email: Type.String(), secret: Type.String(), otpauthUri: Type.String() }),
        },
      },
      config: rateLimit(ctx.config, 'staff-setup', 20, '1 hour'),
    },
    async (request) => service.beginSetup(request.body.token),
  );

  app.post(
    '/auth/setup/complete',
    {
      schema: {
        tags: ['admin'],
        summary: 'Invited staff: set a password (≥ 14 characters) and confirm the first authenticator code.',
        body: Type.Object(
          {
            token: Type.String({ minLength: 20, maxLength: 128 }),
            password: Type.String({ minLength: 1, maxLength: 1024 }),
            totp: Type.String({ pattern: '^[0-9]{6}$' }),
          },
          { additionalProperties: false },
        ),
        response: { 200: Type.Object({ status: Type.Literal('active') }) },
      },
      config: rateLimit(ctx.config, 'staff-setup-complete', 20, '1 hour'),
    },
    async (request) => {
      const { id } = await service.completeSetup(
        request.body.token,
        request.body.password,
        request.body.totp,
      );
      await audit.record({
        actorType: 'staff',
        actorId: id,
        action: 'staff.setup_completed',
        targetType: 'staff',
        targetId: id,
        requestId: request.id,
        ipHash: ctx.hashIp(request.ip),
      });
      return { status: 'active' as const };
    },
  );

  // ------------------------------------------------------------ sessions

  app.post(
    '/auth/login',
    {
      schema: {
        tags: ['admin'],
        summary:
          'Staff sign-in (password + authenticator code). Sets an httpOnly session cookie and returns the CSRF token.',
        body: Type.Object(
          {
            email: Type.String({ maxLength: 254 }),
            password: Type.String({ minLength: 1, maxLength: 1024 }),
            totp: Type.String({ pattern: '^[0-9]{6}$' }),
          },
          { additionalProperties: false },
        ),
        response: { 200: StaffMe },
      },
      config: rateLimit(ctx.config, 'staff-login', 10, '1 minute'),
    },
    async (request, reply) => {
      const session = await service.login(request.body, {
        ip: request.ip,
        userAgent: request.headers['user-agent'],
      });
      reply.setCookie(STAFF_COOKIE, session.token, cookieOptions(session.expiresAt));
      const identity = await service.authenticate(session.token);
      await audit.record({
        actorType: 'staff',
        actorId: session.staffId,
        action: 'staff.login',
        targetType: 'staff',
        targetId: session.staffId,
        requestId: request.id,
        ipHash: ctx.hashIp(request.ip),
      });
      return {
        id: identity!.id,
        email: identity!.email,
        name: identity!.name,
        roles: identity!.roles,
        permissions: [...identity!.permissions].sort(),
        csrfToken: session.csrfToken,
        stepUpValidUntil: new Date(ctx.now().getTime() + STEP_UP_WINDOW_MS).toISOString(),
      };
    },
  );

  app.get(
    '/auth/me',
    {
      schema: {
        tags: ['admin'],
        summary: 'Current staff session.',
        security: [{ staffSession: [] }],
        response: { 200: StaffMe },
      },
      preHandler: requireStaff(),
    },
    async (request) => {
      const staff = staffOf(request);
      return {
        id: staff.id,
        email: staff.email,
        name: staff.name,
        roles: staff.roles,
        permissions: [...staff.permissions].sort(),
        csrfToken: csrfFor(request.staffSessionToken!),
        stepUpValidUntil: staff.stepUpAt
          ? new Date(staff.stepUpAt.getTime() + STEP_UP_WINDOW_MS).toISOString()
          : null,
      };
    },
  );

  app.post(
    '/auth/step-up',
    {
      schema: {
        tags: ['admin'],
        summary: 'Re-confirm with an authenticator code; unlocks dangerous actions for 5 minutes.',
        security: [{ staffSession: [] }],
        body: Type.Object({ totp: Type.String({ pattern: '^[0-9]{6}$' }) }, { additionalProperties: false }),
        response: { 200: Type.Object({ stepUpValidUntil: Type.String({ format: 'date-time' }) }) },
      },
      preHandler: requireStaff(),
      config: rateLimit(ctx.config, 'staff-step-up', 10, '1 minute'),
    },
    async (request) => {
      const staff = staffOf(request);
      const until = await service.stepUp(staff.id, staff.sessionId, request.body.totp);
      await audit.recordStaff(request, { action: 'staff.step_up', targetType: 'staff', targetId: staff.id });
      return { stepUpValidUntil: until.toISOString() };
    },
  );

  app.post(
    '/auth/logout',
    {
      schema: {
        tags: ['admin'],
        summary: 'End the staff session.',
        security: [{ staffSession: [] }],
        response: { 204: Type.Null() },
      },
      preHandler: requireStaff(),
    },
    async (request, reply) => {
      const staff = staffOf(request);
      await service.revokeSession(staff.sessionId);
      await audit.recordStaff(request, { action: 'staff.logout', targetType: 'staff', targetId: staff.id });
      reply.clearCookie(STAFF_COOKIE, { path: '/admin' });
      return reply.status(204).send(null);
    },
  );

  // ------------------------------------------------------------ staff management (staff.manage, step-up)

  app.get(
    '/staff/roles',
    {
      schema: {
        tags: ['admin'],
        summary: 'Roles and their permissions.',
        security: [{ staffSession: [] }],
        response: {
          200: Type.Object({
            permissions: Type.Array(Type.String()),
            roles: Type.Array(
              Type.Object({ id: Type.String(), name: Type.String(), permissions: Type.Array(Type.String()) }),
            ),
          }),
        },
      },
      preHandler: requireStaff(),
    },
    async () => ({ permissions: [...PERMISSIONS], roles: await service.listRoles() }),
  );

  app.get(
    '/staff',
    {
      schema: {
        tags: ['admin'],
        summary: 'List staff users.',
        security: [{ staffSession: [] }],
        response: { 200: Type.Object({ items: Type.Array(StaffView) }) },
      },
      preHandler: requireStaff('staff.manage'),
    },
    async () => ({ items: await service.list() }),
  );

  app.post(
    '/staff',
    {
      schema: {
        tags: ['admin'],
        summary: 'Invite a staff user. Emails a setup link (also returned so it can be shared another way).',
        security: [{ staffSession: [] }],
        body: Type.Object(
          {
            email: Type.String({ maxLength: 254 }),
            name: Type.String({ minLength: 1, maxLength: 100 }),
            roles: Type.Array(Type.String({ maxLength: 64 }), { maxItems: 10 }),
            reason: Reason,
          },
          { additionalProperties: false },
        ),
        response: { 201: Type.Object({ id: Type.String(), setupUrl: Type.String() }) },
      },
      preHandler: requireStaff('staff.manage'),
    },
    async (request, reply) => {
      const { email, name, roles, reason } = request.body;
      const invited = await service.invite({ email, name, roles });
      await service.sendInvite(email, name, invited.setupToken);
      await audit.recordStaff(request, {
        action: 'staff.invite',
        targetType: 'staff',
        targetId: invited.id,
        reason,
        after: { email, name, roles },
      });
      return reply.status(201).send({ id: invited.id, setupUrl: service.setupUrl(invited.setupToken) });
    },
  );

  app.patch(
    '/staff/:id',
    {
      schema: {
        tags: ['admin'],
        summary:
          "Change a staff user's name, roles or status (disable ends their sessions). You cannot change yourself.",
        security: [{ staffSession: [] }],
        params: IdParams,
        body: Type.Object(
          {
            name: Type.Optional(Type.String({ minLength: 1, maxLength: 100 })),
            roles: Type.Optional(Type.Array(Type.String({ maxLength: 64 }), { maxItems: 10 })),
            status: Type.Optional(Type.Union([Type.Literal('active'), Type.Literal('disabled')])),
            reason: Reason,
          },
          { additionalProperties: false },
        ),
        response: { 200: StaffView },
      },
      preHandler: requireStaff('staff.manage'),
    },
    async (request) => {
      if (request.params.id === staffOf(request).id) {
        throw ProblemError.forbidden(
          ErrorCodes.staffCannotModifySelf,
          'Ask another administrator to change your own account.',
        );
      }
      const { reason, ...changes } = request.body;
      const { before, after } = await service.update(request.params.id, changes);
      await audit.recordStaff(request, {
        action: 'staff.update',
        targetType: 'staff',
        targetId: request.params.id,
        reason,
        before,
        after,
      });
      return after;
    },
  );

  app.post(
    '/staff/:id/sessions/revoke',
    {
      schema: {
        tags: ['admin'],
        summary: 'Sign a staff user out everywhere.',
        security: [{ staffSession: [] }],
        params: IdParams,
        body: Type.Object({ reason: Reason }, { additionalProperties: false }),
        response: { 200: Type.Object({ revoked: Type.Integer() }) },
      },
      preHandler: requireStaff('staff.manage'),
    },
    async (request) => {
      const revoked = await service.revokeAllSessions(request.params.id);
      await audit.recordStaff(request, {
        action: 'staff.sessions_revoke',
        targetType: 'staff',
        targetId: request.params.id,
        reason: request.body.reason,
        after: { revoked },
      });
      return { revoked };
    },
  );

  app.post(
    '/staff/:id/reset-credentials',
    {
      schema: {
        tags: ['admin'],
        summary: 'Lost authenticator/password: return the user to "invited" with a new setup link.',
        security: [{ staffSession: [] }],
        params: IdParams,
        body: Type.Object({ reason: Reason }, { additionalProperties: false }),
        response: { 200: Type.Object({ setupUrl: Type.String() }) },
      },
      preHandler: requireStaff('staff.manage'),
    },
    async (request) => {
      if (request.params.id === staffOf(request).id) {
        throw ProblemError.forbidden(
          ErrorCodes.staffCannotModifySelf,
          'Ask another administrator to reset your credentials.',
        );
      }
      const { setupToken } = await service.resetCredentials(request.params.id);
      const target = await service.view(request.params.id);
      if (target) await service.sendInvite(target.email, target.name, setupToken);
      await audit.recordStaff(request, {
        action: 'staff.reset_credentials',
        targetType: 'staff',
        targetId: request.params.id,
        reason: request.body.reason,
      });
      return { setupUrl: service.setupUrl(setupToken) };
    },
  );
};
