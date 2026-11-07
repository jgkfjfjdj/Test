import { maintenanceWindows, newId, remoteConfig, versionPolicy } from '@kindling/db';
import { eq } from 'drizzle-orm';
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { createHarness, type Harness } from './support/harness.js';
import { registerPlayer } from './support/players.js';

let h: Harness;
beforeAll(async () => {
  h = await createHarness();
});
afterAll(async () => {
  await h.close();
});
beforeEach(async () => {
  await h.db.sql`TRUNCATE maintenance_windows, remote_config`;
  await h.db.db
    .update(versionPolicy)
    .set({ minSupportedBuild: 100, latestBuild: 120, storeUrl: 'https://apps.apple.com/app/id000' })
    .where(eq(versionPolicy.platform, 'ios'));
  h.app.liveops.invalidate();
});

async function bootstrap(build?: number) {
  const res = await h.inject({
    method: 'GET',
    url: '/v1/bootstrap',
    headers: build === undefined ? {} : { 'x-build': String(build) },
  });
  expect(res.statusCode).toBe(200);
  return res.json();
}

describe('version gate', () => {
  it.each([
    [90, 'update_required'],
    [100, 'update_available'],
    [119, 'update_available'],
    [120, 'ok'],
    [999, 'ok'],
  ])('build %i → %s', async (build, status) => {
    const body = await bootstrap(build);
    expect(body.version).toEqual({
      status,
      latestBuild: 120,
      minSupportedBuild: 100,
      storeUrl: 'https://apps.apple.com/app/id000',
    });
  });

  it('treats a missing build header as ok (tools, web) and returns server time', async () => {
    const body = await bootstrap();
    expect(body.version.status).toBe('ok');
    expect(Math.abs(Date.parse(body.serverTime) - Date.now())).toBeLessThan(5_000);
  });

  it('refuses sign-in from unsupported builds with 426 and the store URL', async () => {
    const res = await h.inject({
      method: 'POST',
      url: '/v1/auth/login',
      headers: { 'x-build': '42' },
      payload: { email: 'x@example.com', password: 'whatever12345' },
    });
    expect(res.statusCode).toBe(426);
    expect(res.json()).toMatchObject({
      code: 'platform.update_required',
      storeUrl: 'https://apps.apple.com/app/id000',
    });
  });
});

describe('maintenance', () => {
  async function addWindow(
    scope: 'full' | 'login' | 'matchmaking' | 'store',
    startOffsetMs: number,
    endOffsetMs: number | null,
    cancelled = false,
  ) {
    const now = Date.now();
    await h.db.db.insert(maintenanceWindows).values({
      id: newId(),
      scope,
      startsAt: new Date(now + startOffsetMs),
      endsAt: endOffsetMs === null ? null : new Date(now + endOffsetMs),
      message: { en: `${scope} maintenance` },
      cancelledAt: cancelled ? new Date() : null,
    });
    h.app.liveops.invalidate();
  }

  it('reports active and upcoming windows, ignoring ended, cancelled and far-future ones', async () => {
    await addWindow('login', -60_000, 3600_000);
    await addWindow('full', 2 * 3600_000, 3 * 3600_000);
    await addWindow('store', -7200_000, -3600_000); // ended
    await addWindow('full', -60_000, null, true); // cancelled
    await addWindow('full', 3 * 86_400_000, null); // more than 24 h away
    const body = await bootstrap(120);
    expect(body.maintenance.active.map((w: { scope: string }) => w.scope)).toEqual(['login']);
    expect(body.maintenance.upcoming.map((w: { scope: string }) => w.scope)).toEqual(['full']);
  });

  it('blocks sign-in during login maintenance but keeps bootstrap and logout working', async () => {
    const p = await registerPlayer(h);
    await addWindow('login', -60_000, 3600_000);
    const login = await h.inject({
      method: 'POST',
      url: '/v1/auth/login',
      payload: { email: p.email, password: p.password },
    });
    expect(login.statusCode).toBe(503);
    expect(login.json()).toMatchObject({ code: 'platform.maintenance', detail: 'login maintenance' });
    const refresh = await h.inject({
      method: 'POST',
      url: '/v1/auth/refresh',
      payload: { refreshToken: p.refreshToken },
    });
    expect(refresh.statusCode).toBe(503);
    expect((await h.inject({ method: 'GET', url: '/v1/bootstrap' })).statusCode).toBe(200);
    expect(
      (await h.inject({ method: 'POST', url: '/v1/auth/logout', payload: { refreshToken: p.refreshToken } }))
        .statusCode,
    ).toBe(204);
  });

  it('does not block sign-in for store-only maintenance', async () => {
    const p = await registerPlayer(h);
    await addWindow('store', -60_000, 3600_000);
    const login = await h.inject({
      method: 'POST',
      url: '/v1/auth/login',
      payload: { email: p.email, password: p.password },
    });
    expect(login.statusCode).toBe(200);
  });
});

describe('remote config', () => {
  it('serves client-scoped values with a version that changes on update, and supports ETags', async () => {
    await h.db.db.insert(remoteConfig).values([
      { key: 'feature.voiceChat', value: false, scope: 'client' },
      { key: 'quickPlay.maxPingMs', value: 120, scope: 'both' },
      { key: 'server.secretTuning', value: { x: 1 }, scope: 'server' },
    ]);
    const first = await bootstrap();
    expect(first.config.values).toEqual({ 'feature.voiceChat': false, 'quickPlay.maxPingMs': 120 });

    const cfg = await h.inject({ method: 'GET', url: '/v1/config' });
    expect(cfg.json().version).toBe(first.config.version);
    const etag = cfg.headers['etag'] as string;
    const cached = await h.inject({ method: 'GET', url: '/v1/config', headers: { 'if-none-match': etag } });
    expect(cached.statusCode).toBe(304);

    await h.db.db
      .update(remoteConfig)
      .set({ value: true, version: 2 })
      .where(eq(remoteConfig.key, 'feature.voiceChat'));
    const second = await bootstrap();
    expect(second.config.values['feature.voiceChat']).toBe(true);
    expect(second.config.version).not.toBe(first.config.version);
  });
});
