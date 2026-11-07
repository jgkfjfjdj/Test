import type { FastifyRequest } from 'fastify';
import type { AppConfig } from '../config.js';

/**
 * Per-route rate-limit config for @fastify/rate-limit (Redis-backed). Keys are namespaced by
 * `name` so routes don't share buckets. Limits scale with RATE_LIMIT_MULTIPLIER (tests raise it).
 */
export function rateLimit(
  config: AppConfig,
  name: string,
  max: number,
  timeWindow: string,
  key: (request: FastifyRequest) => string = (request) => request.ip,
) {
  return {
    rateLimit: {
      max: Math.ceil(max * config.RATE_LIMIT_MULTIPLIER),
      timeWindow,
      keyGenerator: (request: FastifyRequest) => `${name}:${key(request)}`,
    },
  };
}
