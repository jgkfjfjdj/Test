import {
  BuildInfoResponse,
  LivenessResponse,
  ReadinessResponse,
  type DependencyCheck,
} from '@kindling/contracts';
import type { FastifyPluginAsyncTypebox } from '@fastify/type-provider-typebox';
import type { AppConfig } from '../../config.js';

/** Anything the readiness probe can check. */
export interface Pingable {
  ping(): Promise<void>;
}

export interface HealthRouteOptions {
  config: AppConfig;
  probes: { postgres: Pingable; redis: Pingable };
  startedAt: Date;
  /** Per-dependency timeout for readiness checks. */
  checkTimeoutMs?: number;
}

async function runCheck(probe: Pingable, timeoutMs: number): Promise<DependencyCheck> {
  const start = performance.now();
  let timer: NodeJS.Timeout | undefined;
  try {
    await Promise.race([
      probe.ping(),
      new Promise<never>((_, reject) => {
        timer = setTimeout(() => reject(new Error(`timed out after ${timeoutMs} ms`)), timeoutMs);
      }),
    ]);
    return { status: 'ok', latencyMs: Math.round(performance.now() - start) };
  } catch (err) {
    return {
      status: 'fail',
      latencyMs: Math.round(performance.now() - start),
      error: err instanceof Error ? err.message : String(err),
    };
  } finally {
    if (timer) clearTimeout(timer);
  }
}

export const healthRoutes: FastifyPluginAsyncTypebox<HealthRouteOptions> = async (app, opts) => {
  const timeoutMs = opts.checkTimeoutMs ?? 2_000;

  app.get(
    '/health/live',
    {
      schema: {
        tags: ['health'],
        summary: 'Liveness probe: the process is running.',
        response: { 200: LivenessResponse },
      },
      logLevel: 'warn',
    },
    async () => ({ status: 'ok' as const }),
  );

  app.get(
    '/health/ready',
    {
      schema: {
        tags: ['health'],
        summary: 'Readiness probe: PostgreSQL and Redis are reachable.',
        response: { 200: ReadinessResponse, 503: ReadinessResponse },
      },
      logLevel: 'warn',
    },
    async (_request, reply) => {
      const [postgres, redis] = await Promise.all([
        runCheck(opts.probes.postgres, timeoutMs),
        runCheck(opts.probes.redis, timeoutMs),
      ]);
      const healthy = postgres.status === 'ok' && redis.status === 'ok';
      return reply
        .status(healthy ? 200 : 503)
        .send({ status: healthy ? ('ok' as const) : ('degraded' as const), checks: { postgres, redis } });
    },
  );

  app.get(
    '/health/info',
    {
      schema: {
        tags: ['health'],
        summary: 'Build and runtime information.',
        response: { 200: BuildInfoResponse },
      },
    },
    async () => ({
      service: 'kindling-api',
      version: opts.config.BUILD_VERSION,
      commit: opts.config.BUILD_COMMIT,
      environment: opts.config.APP_ENV,
      startedAt: opts.startedAt.toISOString(),
      uptimeSeconds: Math.round((Date.now() - opts.startedAt.getTime()) / 1000),
    }),
  );
};
