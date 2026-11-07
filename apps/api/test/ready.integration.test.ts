import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import type { FastifyInstance } from 'fastify';
import { buildApp } from '../src/app.js';
import { createRuntimeDeps } from '../src/deps.js';
import { testConfig } from './helpers.js';

const databaseUrl = process.env.TEST_DATABASE_URL;
const redisUrl = process.env.TEST_REDIS_URL;
if (!databaseUrl || !redisUrl) {
  throw new Error(
    'TEST_DATABASE_URL and TEST_REDIS_URL must be set for integration tests (see .env.example).',
  );
}

describe('readiness against real PostgreSQL and Redis', () => {
  let app: FastifyInstance;

  beforeAll(async () => {
    const config = testConfig({ DATABASE_URL: databaseUrl, REDIS_URL: redisUrl });
    const deps = createRuntimeDeps(config);
    await deps.redis.connect();
    app = await buildApp({ config, deps, logger: false });
  });

  afterAll(async () => {
    await app.close();
  });

  it('reports both dependencies as healthy', async () => {
    const res = await app.inject({ method: 'GET', url: '/health/ready' });
    expect(res.statusCode).toBe(200);
    expect(res.json().checks).toMatchObject({ postgres: { status: 'ok' }, redis: { status: 'ok' } });
  });
});
