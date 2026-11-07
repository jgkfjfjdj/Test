import { ErrorCodes } from '@kindling/contracts';
import type { FastifyReply, FastifyRequest } from 'fastify';
import { ProblemError } from '../lib/problem.js';
import { SessionService, type AccessClaims } from '../modules/auth/sessions.js';

declare module 'fastify' {
  interface FastifyRequest {
    player?: AccessClaims;
  }
}

/** preHandler: requires `Authorization: Bearer <access token>` and sets `request.player`. */
export async function requirePlayer(request: FastifyRequest, _reply: FastifyReply): Promise<void> {
  const header = request.headers.authorization;
  if (!header?.startsWith('Bearer ')) {
    throw ProblemError.unauthorized(ErrorCodes.unauthorized, 'Missing bearer token.');
  }
  const sessions = new SessionService(request.server.ctx);
  request.player = await sessions.verifyAccessToken(header.slice('Bearer '.length).trim());
}

export function playerOf(request: FastifyRequest): AccessClaims {
  if (!request.player) throw ProblemError.unauthorized();
  return request.player;
}
