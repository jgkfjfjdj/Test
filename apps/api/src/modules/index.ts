import type { FastifyInstance } from 'fastify';
import { accountPages } from './auth/pages.js';
import { authRoutes } from './auth/routes.js';
import { healthRoutes, type Pingable } from './health/routes.js';
import { meRoutes } from './me/routes.js';

export interface ModuleOptions {
  startedAt: Date;
  probes: { postgres: Pingable; redis: Pingable };
}

/** Registers every route module. New modules are added here as they are implemented. */
export async function registerModules(app: FastifyInstance, options: ModuleOptions): Promise<void> {
  await app.register(healthRoutes, {
    config: app.ctx.config,
    probes: options.probes,
    startedAt: options.startedAt,
  });
  await app.register(authRoutes, { prefix: '/v1/auth' });
  await app.register(meRoutes, { prefix: '/v1/me' });
  await app.register(accountPages);
}
