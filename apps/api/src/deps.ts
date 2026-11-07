import { createDb } from '@kindling/db';
import type { Redis } from 'ioredis';
import type { AppDeps } from './app.js';
import type { AppConfig } from './config.js';
import { createRedis } from './lib/redis.js';

export interface RuntimeDeps extends AppDeps {
  redis: Redis;
  close: () => Promise<void>;
}

/** Creates the real PostgreSQL/Redis connections used by the running service. */
export function createRuntimeDeps(config: AppConfig): RuntimeDeps {
  const database = createDb({
    url: config.DATABASE_URL,
    maxConnections: config.DB_POOL_MAX,
    applicationName: 'kindling-api',
  });
  const redis = createRedis(config.REDIS_URL);

  return {
    redis,
    probes: {
      postgres: {
        ping: async () => {
          await database.sql`SELECT 1`;
        },
      },
      redis: {
        ping: async () => {
          await redis.ping();
        },
      },
    },
    close: async () => {
      await Promise.allSettled([database.close(), redis.quit()]);
    },
  };
}
