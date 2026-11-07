import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import type { FastifyInstance } from 'fastify';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { buildTestApp, testConfig } from './support/unit.js';

let app: FastifyInstance;
beforeAll(async () => {
  const dir = mkdtempSync(join(tmpdir(), 'kindling-web-'));
  mkdirSync(join(dir, 'assets'));
  writeFileSync(join(dir, 'index.html'), '<!doctype html><title>console</title><div id="root"></div>');
  writeFileSync(join(dir, 'assets', 'index-abc123.js'), 'console.log(1)');
  writeFileSync(join(dir, 'favicon.svg'), '<svg/>');
  app = await buildTestApp({}, testConfig({ ADMIN_WEB_DIR: dir }));
});
afterAll(async () => {
  await app.close();
});

describe('staff console hosting under /admin', () => {
  it('serves index.html for the console root and deep links, never cached, with a strict CSP', async () => {
    for (const url of ['/admin/', '/admin/servers/123', '/admin/audit?action=x']) {
      const res = await app.inject({ method: 'GET', url });
      expect(res.statusCode, url).toBe(200);
      expect(res.body).toContain('<div id="root">');
      expect(res.headers['cache-control']).toBe('no-store');
      expect(res.headers['content-security-policy']).toContain("script-src 'self'");
      expect(res.headers['content-security-policy']).toContain("frame-ancestors 'none'");
      expect(res.headers['x-frame-options']).toBe('DENY');
    }
  });

  it('redirects /admin to /admin/', async () => {
    const res = await app.inject({ method: 'GET', url: '/admin' });
    expect(res.statusCode).toBe(308);
    expect(res.headers['location']).toBe('/admin/');
  });

  it('serves hashed assets with long-lived caching and other files with revalidation', async () => {
    const asset = await app.inject({ method: 'GET', url: '/admin/assets/index-abc123.js' });
    expect(asset.statusCode).toBe(200);
    expect(asset.headers['cache-control']).toBe('public, max-age=31536000, immutable');
    const icon = await app.inject({ method: 'GET', url: '/admin/favicon.svg' });
    expect(icon.headers['cache-control']).toBe('no-cache');
  });

  it('keeps API 404s and missing assets as problem+json, not the SPA', async () => {
    const api = await app.inject({ method: 'GET', url: '/admin/v1/does-not-exist' });
    expect(api.statusCode).toBe(404);
    expect(api.headers['content-type']).toContain('application/problem+json');
    const asset = await app.inject({ method: 'GET', url: '/admin/assets/missing.js' });
    expect(asset.statusCode).toBe(404);
  });

  it('does not serve files outside the folder', async () => {
    const res = await app.inject({ method: 'GET', url: '/admin/../package.json' });
    expect(res.body).not.toContain('"name"');
  });
});
