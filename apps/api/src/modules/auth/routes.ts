import type { FastifyPluginAsyncTypebox } from '@fastify/type-provider-typebox';
import type { FastifyRequest } from 'fastify';
import { Type } from 'typebox';
import { rateLimit } from '../../lib/rate-limit.js';
import { clientInfo } from '../liveops/service.js';
import { playerOf, requirePlayer } from '../../plugins/player-auth.js';
import {
  AcceptedResponse,
  AppleBody,
  ForgotBody,
  LoginBody,
  RefreshBody,
  RefreshResponse,
  RegisterBody,
  ResetBody,
  SessionResponse,
  TokenBody,
} from './schemas.js';
import { AuthService } from './service.js';
import type { SessionMeta } from './sessions.js';

function sessionMeta(request: FastifyRequest): SessionMeta {
  const version = request.headers['x-client-version'];
  return {
    ip: request.ip,
    userAgent: request.headers['user-agent'],
    clientVersion: typeof version === 'string' ? version.slice(0, 32) : undefined,
  };
}

export const authRoutes: FastifyPluginAsyncTypebox = async (app) => {
  const { config } = app.ctx;
  const auth = new AuthService(app.ctx);

  /** Sign-in style routes are closed during login/full maintenance and for unsupported builds. */
  const signInGate = async (request: FastifyRequest) => {
    await app.liveops.assertAvailable('login');
    await app.liveops.assertSupportedBuild(clientInfo(request));
  };

  app.post(
    '/register',
    {
      schema: {
        tags: ['auth'],
        summary: 'Create an account with email + password. Sends a verification email and signs in.',
        body: RegisterBody,
        response: { 201: SessionResponse },
      },
      preHandler: signInGate,
      config: rateLimit(config, 'register', 5, '1 hour'),
    },
    async (request, reply) => reply.status(201).send(await auth.register(request.body, sessionMeta(request))),
  );

  app.post(
    '/login',
    {
      schema: {
        tags: ['auth'],
        summary: 'Sign in with email + password.',
        body: LoginBody,
        response: { 200: SessionResponse },
      },
      preHandler: signInGate,
      config: rateLimit(config, 'login', 20, '1 minute'),
    },
    async (request) => auth.login(request.body, sessionMeta(request)),
  );

  app.post(
    '/apple',
    {
      schema: {
        tags: ['auth'],
        summary:
          'Sign in with Apple. New players must also send displayName (422 account.display_name_required otherwise).',
        body: AppleBody,
        response: { 200: SessionResponse, 201: SessionResponse },
      },
      preHandler: signInGate,
      config: rateLimit(config, 'apple', 20, '1 minute'),
    },
    async (request, reply) => {
      const result = await auth.signInWithApple(request.body, sessionMeta(request));
      return reply.status(result.created ? 201 : 200).send(result);
    },
  );

  app.post(
    '/refresh',
    {
      schema: {
        tags: ['auth'],
        summary:
          'Exchange a refresh token for a new access + refresh token. Reusing a refresh token ends the session.',
        body: RefreshBody,
        response: { 200: RefreshResponse },
      },
      preHandler: signInGate,
      config: rateLimit(config, 'refresh', 60, '1 minute'),
    },
    async (request) => {
      const { accountId: _accountId, ...tokens } = await auth.sessions.refresh(
        request.body.refreshToken,
        sessionMeta(request),
      );
      return tokens;
    },
  );

  app.post(
    '/logout',
    {
      schema: {
        tags: ['auth'],
        summary: 'End this session (idempotent).',
        body: RefreshBody,
        response: { 204: Type.Null() },
      },
      config: rateLimit(config, 'logout', 60, '1 minute'),
    },
    async (request, reply) => {
      await auth.sessions.revokeByRefreshToken(request.body.refreshToken);
      return reply.status(204).send(null);
    },
  );

  app.post(
    '/verify-email',
    {
      schema: {
        tags: ['auth'],
        summary: 'Confirm an email address with the emailed token.',
        body: TokenBody,
        response: { 200: AcceptedResponse },
      },
      config: rateLimit(config, 'verify-email', 30, '1 hour'),
    },
    async (request) => {
      await auth.verifyEmail(request.body.token);
      return { status: 'verified' };
    },
  );

  app.post(
    '/verify-email/resend',
    {
      schema: {
        tags: ['auth'],
        summary: 'Send the verification email again.',
        security: [{ bearer: [] }],
        response: { 200: AcceptedResponse },
      },
      preHandler: requirePlayer,
      config: rateLimit(
        config,
        'verify-resend',
        3,
        '1 hour',
        (request) => request.headers.authorization ?? request.ip,
      ),
    },
    async (request) => ({ status: await auth.resendVerification(playerOf(request).accountId) }),
  );

  app.post(
    '/password/forgot',
    {
      schema: {
        tags: ['auth'],
        summary:
          'Request a password-reset email. Always returns 202 (does not reveal whether the email exists).',
        body: ForgotBody,
        response: { 202: AcceptedResponse },
      },
      config: rateLimit(config, 'forgot', 5, '1 hour'),
    },
    async (request, reply) => {
      await auth.forgotPassword(request.body.email);
      return reply.status(202).send({ status: 'accepted' });
    },
  );

  app.post(
    '/password/reset',
    {
      schema: {
        tags: ['auth'],
        summary: 'Set a new password with the emailed token. Signs out every session.',
        body: ResetBody,
        response: { 200: AcceptedResponse },
      },
      config: rateLimit(config, 'reset', 10, '1 hour'),
    },
    async (request) => {
      await auth.resetPassword(request.body.token, request.body.newPassword);
      return { status: 'password_changed' };
    },
  );
};
