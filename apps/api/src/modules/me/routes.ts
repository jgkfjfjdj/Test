import type { FastifyPluginAsyncTypebox } from '@fastify/type-provider-typebox';
import { ErrorCodes } from '@kindling/contracts';
import { CLOUD_SAVE_SLOTS } from '@kindling/db';
import { Type } from 'typebox';
import { ProblemError } from '../../lib/problem.js';
import { rateLimit } from '../../lib/rate-limit.js';
import { playerOf, requirePlayer } from '../../plugins/player-auth.js';
import { AccountService, isCloudSaveSlot } from '../accounts/service.js';

const MeResponse = Type.Object({
  id: Type.String({ format: 'uuid' }),
  displayName: Type.String(),
  email: Type.Union([Type.String(), Type.Null()]),
  emailVerified: Type.Boolean(),
  status: Type.String(),
  createdAt: Type.String({ format: 'date-time' }),
  deletion: Type.Union([
    Type.Object({
      requestedAt: Type.String({ format: 'date-time' }),
      purgeAfter: Type.String({ format: 'date-time' }),
    }),
    Type.Null(),
  ]),
  profile: Type.Object({
    level: Type.Integer(),
    xp: Type.Integer(),
    loadout: Type.Record(Type.String(), Type.String()),
  }),
});

const CloudSaveResponse = Type.Object({
  slot: Type.String(),
  version: Type.Integer({ minimum: 1 }),
  data: Type.Unknown(),
  updatedAt: Type.String({ format: 'date-time' }),
});

const SlotParams = Type.Object({ slot: Type.Union(CLOUD_SAVE_SLOTS.map((s) => Type.Literal(s))) });

export const meRoutes: FastifyPluginAsyncTypebox = async (app) => {
  const { config } = app.ctx;
  const accountsService = new AccountService(app.ctx);
  app.addHook('preHandler', requirePlayer);

  app.get(
    '',
    {
      schema: {
        tags: ['me'],
        summary: 'The signed-in account and profile.',
        security: [{ bearer: [] }],
        response: { 200: MeResponse },
      },
    },
    async (request) => accountsService.me(playerOf(request).accountId),
  );

  app.patch(
    '',
    {
      schema: {
        tags: ['me'],
        summary: 'Change the display name.',
        security: [{ bearer: [] }],
        body: Type.Object(
          { displayName: Type.String({ minLength: 1, maxLength: 32 }) },
          { additionalProperties: false },
        ),
        response: { 200: MeResponse },
      },
      config: rateLimit(config, 'rename', 5, '1 day', (request) => request.player?.accountId ?? request.ip),
    },
    async (request) => accountsService.rename(playerOf(request).accountId, request.body.displayName),
  );

  app.delete(
    '',
    {
      schema: {
        tags: ['me'],
        summary: `Request account deletion. The account is purged after a grace period and every session ends now.`,
        security: [{ bearer: [] }],
        body: Type.Object({ confirm: Type.Literal('DELETE') }, { additionalProperties: false }),
        response: {
          202: Type.Object({
            status: Type.Literal('pending_deletion'),
            purgeAfter: Type.String({ format: 'date-time' }),
          }),
        },
      },
    },
    async (request, reply) => {
      const result = await accountsService.requestDeletion(playerOf(request).accountId);
      return reply.status(202).send({ status: 'pending_deletion' as const, ...result });
    },
  );

  app.post(
    '/deletion/cancel',
    {
      schema: {
        tags: ['me'],
        summary: 'Cancel a pending account deletion (sign in again first).',
        security: [{ bearer: [] }],
        response: { 200: MeResponse },
      },
    },
    async (request) => accountsService.cancelDeletion(playerOf(request).accountId),
  );

  app.get(
    '/cloud-saves/:slot',
    {
      schema: {
        tags: ['me'],
        summary: 'Read a cloud save slot.',
        security: [{ bearer: [] }],
        params: SlotParams,
        response: { 200: CloudSaveResponse },
      },
    },
    async (request) => {
      const { slot } = request.params;
      if (!isCloudSaveSlot(slot)) throw ProblemError.notFound(undefined, ErrorCodes.cloudSaveNotFound);
      return accountsService.getCloudSave(playerOf(request).accountId, slot);
    },
  );

  app.put(
    '/cloud-saves/:slot',
    {
      schema: {
        tags: ['me'],
        summary:
          'Write a cloud save slot. `baseVersion` is the version the client last read (0 for a new slot); 409 returns the current copy.',
        security: [{ bearer: [] }],
        params: SlotParams,
        body: Type.Object(
          { baseVersion: Type.Integer({ minimum: 0 }), data: Type.Unknown() },
          { additionalProperties: false },
        ),
        response: { 200: CloudSaveResponse },
      },
      config: rateLimit(
        config,
        'cloud-save',
        120,
        '1 minute',
        (request) => request.player?.accountId ?? request.ip,
      ),
    },
    async (request) => {
      const { slot } = request.params;
      if (!isCloudSaveSlot(slot)) throw ProblemError.notFound(undefined, ErrorCodes.cloudSaveNotFound);
      return accountsService.putCloudSave(
        playerOf(request).accountId,
        slot,
        request.body.baseVersion,
        request.body.data,
      );
    },
  );
};
