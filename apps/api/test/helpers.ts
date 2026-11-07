import type { FastifyInstance } from 'fastify';
import { buildApp, type AppDeps } from '../src/app.js';
import { loadConfig, type AppConfig } from '../src/config.js';

export function testConfig(overrides: Record<string, string> = {}): AppConfig {
  return loadConfig({
    NODE_ENV: 'test',
    APP_ENV: 'test',
    DATABASE_URL: 'postgres://unused:unused@127.0.0.1:1/unused',
    REDIS_URL: 'redis://127.0.0.1:1/0',
    BUILD_VERSION: '1.2.3-test',
    BUILD_COMMIT: 'abc1234',
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

export async function buildTestApp(deps: Partial<AppDeps['probes']> = {}): Promise<FastifyInstance> {
  return buildApp({
    config: testConfig(),
    logger: false,
    deps: { probes: { postgres: okProbe, redis: okProbe, ...deps } },
  });
}
