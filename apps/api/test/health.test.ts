import { afterEach, describe, expect, it } from 'vitest';
import type { FastifyInstance } from 'fastify';
import { buildTestApp, failingProbe, hangingProbe } from './support/unit.js';

let app: FastifyInstance | undefined;
afterEach(async () => {
  await app?.close();
  app = undefined;
});

describe('health endpoints', () => {
  it('GET /health/live returns ok', async () => {
    app = await buildTestApp();
    const res = await app.inject({ method: 'GET', url: '/health/live' });
    expect(res.statusCode).toBe(200);
    expect(res.json()).toEqual({ status: 'ok' });
  });

  it('GET /health/ready returns 200 when all dependencies respond', async () => {
    app = await buildTestApp();
    const res = await app.inject({ method: 'GET', url: '/health/ready' });
    expect(res.statusCode).toBe(200);
    const body = res.json();
    expect(body.status).toBe('ok');
    expect(body.checks.postgres.status).toBe('ok');
    expect(body.checks.redis.status).toBe('ok');
  });

  it('GET /health/ready returns 503 and names the failing dependency', async () => {
    app = await buildTestApp({ redis: failingProbe('ECONNREFUSED') });
    const res = await app.inject({ method: 'GET', url: '/health/ready' });
    expect(res.statusCode).toBe(503);
    const body = res.json();
    expect(body.status).toBe('degraded');
    expect(body.checks.postgres.status).toBe('ok');
    expect(body.checks.redis).toMatchObject({ status: 'fail', error: 'ECONNREFUSED' });
  });

  it('GET /health/ready times out a hanging dependency instead of hanging', async () => {
    app = await buildTestApp({ postgres: hangingProbe });
    const started = Date.now();
    const res = await app.inject({ method: 'GET', url: '/health/ready' });
    expect(res.statusCode).toBe(503);
    expect(res.json().checks.postgres.error).toMatch(/timed out/);
    expect(Date.now() - started).toBeLessThan(5_000);
  });

  it('GET /health/info reports build information', async () => {
    app = await buildTestApp();
    const res = await app.inject({ method: 'GET', url: '/health/info' });
    expect(res.statusCode).toBe(200);
    expect(res.json()).toMatchObject({
      service: 'kindling-api',
      version: '1.2.3-test',
      commit: 'abc1234',
      environment: 'test',
    });
  });
});
