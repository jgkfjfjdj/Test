import { createDb } from '@kindling/db';
import type { FastifyInstance } from 'fastify';
import { buildApp } from '../../src/app.js';
import { loadConfig, type AppConfig } from '../../src/config.js';
import type { AppDeps } from '../../src/context.js';
import type { AppleTokenVerifier } from '../../src/lib/apple.js';
import { KeyRing } from '../../src/lib/keys.js';
import { MemoryMailer } from '../../src/lib/mailer.js';
import { noBreachCheck } from '../../src/lib/passwords.js';
import { createRedis } from '../../src/lib/redis.js';

export function testConfig(overrides: Record<string, string> = {}): AppConfig {
  return loadConfig({
    NODE_ENV: 'test',
    APP_ENV: 'test',
    DATABASE_URL: 'postgres://unused:unused@127.0.0.1:1/unused',
    REDIS_URL: 'redis://127.0.0.1:1/0',
    BUILD_VERSION: '1.2.3-test',
    BUILD_COMMIT: 'abc1234',
    ADMIN_COOKIE_SECURE: 'false',
    RATE_LIMIT_MULTIPLIER: '1000',
    ...overrides,
  });
}

export const okProbe = { ping: async () => {} };
export const failingProbe = (message = 'connection refused') => ({
  ping: async () => {
    throw new Error(message);
  },
});
export const hangingProbe = { ping: () => new Promise<void>(() => {}) };

export const rejectingApple: AppleTokenVerifier = {
  verify: async () => {
    throw new Error('Apple verification is not available in unit tests');
  },
};

/**
 * App for tests that don't touch PostgreSQL/Redis. The clients are created lazily and never connect
 * (postgres.js connects on first query; ioredis uses lazyConnect).
 */
export async function buildTestApp(
  probes: Partial<AppDeps['probes']> = {},
  config: AppConfig = testConfig(),
): Promise<FastifyInstance> {
  const db = createDb({ url: config.DATABASE_URL, maxConnections: 1 });
  const redis = createRedis(config.REDIS_URL);
  return buildApp({
    config,
    logger: false,
    deps: {
      db,
      redis,
      mailer: new MemoryMailer(),
      keys: {
        access: (await KeyRing.fromEnv('kindling-api', undefined)).ring,
        tickets: (await KeyRing.fromEnv('kindling-tickets', undefined)).ring,
      },
      apple: rejectingApple,
      breachChecker: noBreachCheck,
      probes: { postgres: okProbe, redis: okProbe, ...probes },
      close: async () => {
        redis.disconnect();
        await db.close(0);
      },
    },
  });
}
