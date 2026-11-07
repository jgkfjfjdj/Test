import { Type } from 'typebox';
import { afterEach, describe, expect, it } from 'vitest';
import type { FastifyInstance } from 'fastify';
import { ProblemError } from '../src/lib/problem.js';
import { buildTestApp } from './helpers.js';

let app: FastifyInstance | undefined;
afterEach(async () => {
  await app?.close();
  app = undefined;
});

async function appWithTestRoutes(): Promise<FastifyInstance> {
  const instance = await buildTestApp();
  instance.post(
    '/test/validate',
    {
      schema: { body: Type.Object({ name: Type.String({ minLength: 3 }) }, { additionalProperties: false }) },
    },
    async () => ({ ok: true }),
  );
  instance.get('/test/problem', async () => {
    throw new ProblemError(409, 'test.conflict', 'Conflict', 'Already exists');
  });
  instance.get('/test/crash', async () => {
    throw new Error('secret internal detail');
  });
  return instance;
}

describe('HTTP conventions', () => {
  it('unknown routes return problem+json 404', async () => {
    app = await buildTestApp();
    const res = await app.inject({ method: 'GET', url: '/nope?x=1' });
    expect(res.statusCode).toBe(404);
    expect(res.headers['content-type']).toContain('application/problem+json');
    expect(res.json()).toMatchObject({
      status: 404,
      code: 'common.not_found',
      detail: 'No route for GET /nope',
    });
  });

  it('validation errors return 400 with field paths', async () => {
    app = await appWithTestRoutes();
    const res = await app.inject({ method: 'POST', url: '/test/validate', payload: { name: 'ab' } });
    expect(res.statusCode).toBe(400);
    const body = res.json();
    expect(body.code).toBe('common.validation_failed');
    expect(body.errors[0].path).toBe('body/name');
  });

  it('ProblemError is rendered as thrown', async () => {
    app = await appWithTestRoutes();
    const res = await app.inject({ method: 'GET', url: '/test/problem' });
    expect(res.statusCode).toBe(409);
    expect(res.json()).toMatchObject({ code: 'test.conflict', title: 'Conflict', detail: 'Already exists' });
  });

  it('unexpected errors become a generic 500 without leaking details', async () => {
    app = await appWithTestRoutes();
    const res = await app.inject({ method: 'GET', url: '/test/crash' });
    expect(res.statusCode).toBe(500);
    expect(res.json().code).toBe('common.internal_error');
    expect(res.body).not.toContain('secret internal detail');
  });

  it('malformed JSON bodies return 400 problem+json', async () => {
    app = await appWithTestRoutes();
    const res = await app.inject({
      method: 'POST',
      url: '/test/validate',
      headers: { 'content-type': 'application/json' },
      payload: '{"name":',
    });
    expect(res.statusCode).toBe(400);
    expect(res.headers['content-type']).toContain('application/problem+json');
  });

  it('echoes a safe incoming request id and replaces unsafe ones', async () => {
    app = await buildTestApp();
    const safe = await app.inject({
      method: 'GET',
      url: '/health/live',
      headers: { 'x-request-id': 'req-12345678' },
    });
    expect(safe.headers['x-request-id']).toBe('req-12345678');
    const unsafe = await app.inject({
      method: 'GET',
      url: '/health/live',
      headers: { 'x-request-id': 'bad id\nwith newline' },
    });
    expect(unsafe.headers['x-request-id']).not.toContain('bad id');
    expect(String(unsafe.headers['x-request-id'])).toMatch(/^[0-9a-f-]{36}$/);
  });

  it('sets baseline security headers', async () => {
    app = await buildTestApp();
    const res = await app.inject({ method: 'GET', url: '/health/live' });
    expect(res.headers['x-content-type-options']).toBe('nosniff');
    expect(res.headers['cache-control']).toBe('no-store');
  });

  it('serves an OpenAPI 3.1 document containing the health routes', async () => {
    app = await buildTestApp();
    await app.ready();
    const doc = app.swagger() as { openapi: string; paths: Record<string, unknown> };
    expect(doc.openapi).toBe('3.1.0');
    expect(Object.keys(doc.paths)).toEqual(
      expect.arrayContaining(['/health/live', '/health/ready', '/health/info']),
    );
  });
});
