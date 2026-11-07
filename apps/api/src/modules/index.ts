import type { FastifyInstance } from 'fastify';
import { adminLiveOpsRoutes } from './admin/liveops-routes.js';
import { auditRoutes } from './audit/routes.js';
import { accountPages } from './auth/pages.js';
import { authRoutes } from './auth/routes.js';
import { healthRoutes, type Pingable } from './health/routes.js';
import { liveOpsRoutes } from './liveops/routes.js';
import { LiveOpsService } from './liveops/service.js';
import { meRoutes } from './me/routes.js';
import { staffRoutes } from './staff/routes.js';

export interface ModuleOptions {
  startedAt: Date;
  probes: { postgres: Pingable; redis: Pingable };
}

/** Registers every route module. New modules are added here as they are implemented. */
declare module 'fastify' {
  interface FastifyInstance {
    /** Shared so its short-lived caches (and their invalidation) are per app instance. */
    liveops: LiveOpsService;
  }
}

export async function registerModules(app: FastifyInstance, options: ModuleOptions): Promise<void> {
  app.decorate('liveops', new LiveOpsService(app.ctx));
  await app.register(healthRoutes, {
    config: app.ctx.config,
    probes: options.probes,
    startedAt: options.startedAt,
  });
  await app.register(liveOpsRoutes, { prefix: '/v1' });
  await app.register(authRoutes, { prefix: '/v1/auth' });
  await app.register(meRoutes, { prefix: '/v1/me' });
  await app.register(accountPages);
  await app.register(
    async (admin) => {
      await admin.register(staffRoutes);
      await admin.register(auditRoutes);
      await admin.register(adminLiveOpsRoutes);
    },
    { prefix: '/admin/v1' },
  );
}
