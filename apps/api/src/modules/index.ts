import type { FastifyInstance } from 'fastify';
import { adminLiveOpsRoutes } from './admin/liveops-routes.js';
import { registerAdminWeb } from './admin-web/plugin.js';
import { auditRoutes } from './audit/routes.js';
import { accountPages } from './auth/pages.js';
import { authRoutes } from './auth/routes.js';
import { healthRoutes, type Pingable } from './health/routes.js';
import { liveOpsRoutes } from './liveops/routes.js';
import { LiveOpsService } from './liveops/service.js';
import { meRoutes } from './me/routes.js';
import { adminServerRoutes } from './servers/admin-routes.js';
import { LinkHub } from './servers/link-hub.js';
import { serverLinkRoutes } from './servers/link-routes.js';
import { serverBrowserRoutes } from './servers/routes.js';
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
    /** Server-link connections on this instance + cross-instance command routing. */
    linkHub: LinkHub;
  }
}

export async function registerModules(app: FastifyInstance, options: ModuleOptions): Promise<void> {
  app.decorate('liveops', new LiveOpsService(app.ctx));
  app.decorate('linkHub', new LinkHub(app.ctx.redis, app.log));
  // preClose runs before connections and dependencies are closed: drop server links first.
  app.addHook('preClose', async () => {
    await app.linkHub.close();
  });
  await app.register(healthRoutes, {
    config: app.ctx.config,
    probes: options.probes,
    startedAt: options.startedAt,
  });
  await app.register(liveOpsRoutes, { prefix: '/v1' });
  await app.register(authRoutes, { prefix: '/v1/auth' });
  await app.register(meRoutes, { prefix: '/v1/me' });
  await app.register(serverBrowserRoutes, { prefix: '/v1/servers' });
  await app.register(serverLinkRoutes, { prefix: '/server/v1' });
  await app.register(accountPages);
  await app.register(
    async (admin) => {
      await admin.register(staffRoutes);
      await admin.register(auditRoutes);
      await admin.register(adminLiveOpsRoutes);
      await admin.register(adminServerRoutes);
    },
    { prefix: '/admin/v1' },
  );
  await registerAdminWeb(app, app.ctx.config.ADMIN_WEB_DIR);
}
