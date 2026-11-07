import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { base32Decode } from '../src/lib/totp.js';
import { StaffService } from '../src/modules/staff/service.js';
import { createHarness, type Harness } from './support/harness.js';
import { createStaff, nextCode, staffCookies, staffHeaders } from './support/staff.js';

let h: Harness;
beforeAll(async () => {
  h = await createHarness();
});
afterAll(async () => {
  await h.close();
});

describe('staff setup', () => {
  it('requires a valid authenticator code and a strong password, and the link works once', async () => {
    const service = new StaffService(h.app.ctx);
    const { setupToken } = await service.invite({
      email: 'setup@studio.example',
      name: 'Setup',
      roles: ['moderator'],
    });
    const early = await h.inject({
      method: 'POST',
      url: '/admin/v1/auth/setup/complete',
      payload: { token: setupToken, password: 'a long staff passphrase 42', totp: '123456' },
    });
    expect(early.json().code).toBe('staff.setup_invalid'); // begin not called yet

    const begin = await h.inject({
      method: 'POST',
      url: '/admin/v1/auth/setup/begin',
      payload: { token: setupToken },
    });
    expect(begin.json().otpauthUri).toMatch(/^otpauth:\/\/totp\//);
    const secret = base32Decode(begin.json().secret);

    const wrongCode = await h.inject({
      method: 'POST',
      url: '/admin/v1/auth/setup/complete',
      payload: { token: setupToken, password: 'a long staff passphrase 42', totp: '000000' },
    });
    expect(wrongCode.json().code).toBe('staff.invalid_credentials');
    const weak = await h.inject({
      method: 'POST',
      url: '/admin/v1/auth/setup/complete',
      payload: { token: setupToken, password: 'only12chars!', totp: nextCode(h, secret) },
    });
    expect(weak.json().code).toBe('auth.password_weak');
    const ok = await h.inject({
      method: 'POST',
      url: '/admin/v1/auth/setup/complete',
      payload: { token: setupToken, password: 'a long staff passphrase 42', totp: nextCode(h, secret) },
    });
    expect(ok.statusCode).toBe(200);
    const reuse = await h.inject({
      method: 'POST',
      url: '/admin/v1/auth/setup/begin',
      payload: { token: setupToken },
    });
    expect(reuse.json().code).toBe('staff.setup_invalid');
  });
});

describe('staff sessions', () => {
  it('sets a strict httpOnly cookie, exposes /auth/me, and rejects replayed codes', async () => {
    const s = await createStaff(h, ['moderator']);
    const me = await h.inject({ method: 'GET', url: '/admin/v1/auth/me', cookies: staffCookies(s) });
    expect(me.statusCode).toBe(200);
    expect(me.json()).toMatchObject({ id: s.id, roles: ['moderator'], csrfToken: s.csrf });
    expect(me.json().permissions).toContain('players.sanction');

    const code = nextCode(h, s.secret);
    const login = await h.inject({
      method: 'POST',
      url: '/admin/v1/auth/login',
      payload: { email: s.email, password: s.password, totp: code },
    });
    expect(login.statusCode).toBe(200);
    const cookie = login.cookies.find((c) => c.name === 'kindling_staff')!;
    expect(cookie).toMatchObject({ httpOnly: true, sameSite: 'Strict', path: '/admin' });
    const replay = await h.inject({
      method: 'POST',
      url: '/admin/v1/auth/login',
      payload: { email: s.email, password: s.password, totp: code },
    });
    expect(replay.statusCode).toBe(401);
  });

  it('rejects wrong passwords/codes uniformly and requires the cookie', async () => {
    const s = await createStaff(h, ['analyst']);
    const wrongPw = await h.inject({
      method: 'POST',
      url: '/admin/v1/auth/login',
      payload: { email: s.email, password: 'nope nope nope nope', totp: nextCode(h, s.secret) },
    });
    const wrongCode = await h.inject({
      method: 'POST',
      url: '/admin/v1/auth/login',
      payload: { email: s.email, password: s.password, totp: '000000' },
    });
    expect(wrongPw.json().code).toBe('staff.invalid_credentials');
    expect(wrongCode.json().code).toBe('staff.invalid_credentials');
    expect(wrongPw.json().detail).toBe(wrongCode.json().detail);
    expect((await h.inject({ method: 'GET', url: '/admin/v1/auth/me' })).json().code).toBe(
      'staff.unauthorized',
    );
    // Player access tokens are not staff credentials.
    expect(
      (await h.inject({ method: 'GET', url: '/admin/v1/auth/me', headers: { authorization: 'Bearer x' } }))
        .statusCode,
    ).toBe(401);
  });

  it('requires the CSRF header on state-changing requests', async () => {
    const s = await createStaff(h, ['analyst']);
    const noCsrf = await h.inject({ method: 'POST', url: '/admin/v1/auth/logout', cookies: staffCookies(s) });
    expect(noCsrf.statusCode).toBe(403);
    expect(noCsrf.json().code).toBe('staff.csrf_failed');
    const badCsrf = await h.inject({
      method: 'POST',
      url: '/admin/v1/auth/logout',
      cookies: staffCookies(s),
      headers: { 'x-csrf-token': 'forged' },
    });
    expect(badCsrf.statusCode).toBe(403);
    const ok = await h.inject({
      method: 'POST',
      url: '/admin/v1/auth/logout',
      cookies: staffCookies(s),
      headers: staffHeaders(s),
    });
    expect(ok.statusCode).toBe(204);
    expect(
      (await h.inject({ method: 'GET', url: '/admin/v1/auth/me', cookies: staffCookies(s) })).statusCode,
    ).toBe(401);
  });

  it('ends idle sessions after 30 minutes', async () => {
    const s = await createStaff(h, ['analyst']);
    h.clock.advance(31 * 60_000);
    expect(
      (await h.inject({ method: 'GET', url: '/admin/v1/auth/me', cookies: staffCookies(s) })).statusCode,
    ).toBe(401);
  });
});

describe('RBAC and step-up', () => {
  it('denies actions outside the role', async () => {
    const analyst = await createStaff(h, ['analyst']);
    const res = await h.inject({ method: 'GET', url: '/admin/v1/staff', cookies: staffCookies(analyst) });
    expect(res.statusCode).toBe(403);
    expect(res.json().code).toBe('staff.forbidden');
    expect(
      (await h.inject({ method: 'GET', url: '/admin/v1/audit', cookies: staffCookies(analyst) })).statusCode,
    ).toBe(403);
  });

  it('requires a fresh authenticator confirmation for dangerous actions', async () => {
    const admin = await createStaff(h, ['super_admin']);
    const invite = (email: string) =>
      h.inject({
        method: 'POST',
        url: '/admin/v1/staff',
        cookies: staffCookies(admin),
        headers: staffHeaders(admin),
        payload: { email, name: 'New Mod', roles: ['moderator'], reason: 'growing the team' },
      });
    expect((await invite('fresh1@studio.example')).statusCode).toBe(201); // login counts as step-up
    h.clock.advance(6 * 60_000);
    const stale = await invite('fresh2@studio.example');
    expect(stale.statusCode).toBe(403);
    expect(stale.json().code).toBe('staff.step_up_required');
    // Reads don't need step-up.
    expect(
      (await h.inject({ method: 'GET', url: '/admin/v1/staff', cookies: staffCookies(admin) })).statusCode,
    ).toBe(200);
    const step = await h.inject({
      method: 'POST',
      url: '/admin/v1/auth/step-up',
      cookies: staffCookies(admin),
      headers: staffHeaders(admin),
      payload: { totp: nextCode(h, admin.secret) },
    });
    expect(step.statusCode).toBe(200);
    const again = await invite('fresh2@studio.example');
    expect(again.statusCode).toBe(201);
    expect(again.json().setupUrl).toMatch(/^https:\/\/api\.test\.invalid\/admin\/setup\?token=/);
    expect(h.mailer.lastTo('fresh2@studio.example')?.subject).toContain('staff console invitation');
  });
});

describe('staff management', () => {
  it('changes roles, refuses self-modification, and disabling ends sessions — all audited', async () => {
    const admin = await createStaff(h, ['super_admin']);
    const mod = await createStaff(h, ['moderator']);
    const patch = (id: string, payload: Record<string, unknown>) =>
      h.inject({
        method: 'PATCH',
        url: `/admin/v1/staff/${id}`,
        cookies: staffCookies(admin),
        headers: staffHeaders(admin),
        payload,
      });

    const promoted = await patch(mod.id, {
      roles: ['moderator', 'support'],
      reason: 'covers support shifts',
    });
    expect(promoted.json().roles).toEqual(['moderator', 'support']);
    expect((await patch(admin.id, { roles: [], reason: 'oops' })).json().code).toBe(
      'staff.cannot_modify_self',
    );
    expect((await patch(mod.id, { roles: ['god_mode'], reason: 'nope' })).json().code).toBe(
      'staff.unknown_role',
    );
    expect((await patch(mod.id, { status: 'disabled' })).statusCode).toBe(400); // reason required

    expect((await patch(mod.id, { status: 'disabled', reason: 'left the studio' })).json().status).toBe(
      'disabled',
    );
    expect(
      (await h.inject({ method: 'GET', url: '/admin/v1/auth/me', cookies: staffCookies(mod) })).statusCode,
    ).toBe(401);

    const audit = await h.inject({
      method: 'GET',
      url: `/admin/v1/audit?targetId=${mod.id}&action=staff.update`,
      cookies: staffCookies(admin),
    });
    const items = audit.json().items;
    expect(items).toHaveLength(2);
    expect(items[0]).toMatchObject({
      actorType: 'staff',
      actorId: admin.id,
      reason: 'left the studio',
      after: { status: 'disabled' },
    });
    expect(items[1]).toMatchObject({
      reason: 'covers support shifts',
      before: { roles: ['moderator'] },
      after: { roles: ['moderator', 'support'] },
    });
  });

  it('paginates the audit log with a cursor', async () => {
    const admin = await createStaff(h, ['super_admin']);
    const seen = new Set<string>();
    let cursor: string | null = null;
    let pages = 0;
    do {
      const query: string = cursor ? `&cursor=${cursor}` : '';
      const res = await h.inject({
        method: 'GET',
        url: `/admin/v1/audit?limit=3${query}`,
        cookies: staffCookies(admin),
      });
      expect(res.statusCode).toBe(200);
      for (const item of res.json().items) {
        expect(seen.has(item.id)).toBe(false);
        seen.add(item.id);
      }
      cursor = res.json().nextCursor;
      pages += 1;
    } while (cursor && pages < 50);
    const total = await h.db.sql<{ n: number }[]>`SELECT count(*)::int AS n FROM audit_log`;
    expect(seen.size).toBe(total[0]?.n);
    expect(pages).toBeGreaterThan(1);
  });
});

describe('admin live ops (drives /v1/bootstrap)', () => {
  it('updates version policy, schedules and cancels maintenance, and edits remote config', async () => {
    const ops = await createStaff(h, ['server_ops']);
    const live = await createStaff(h, ['liveops']);
    const as = (s: typeof ops) => ({ cookies: staffCookies(s), headers: staffHeaders(s) });

    const policy = await h.inject({
      method: 'PUT',
      url: '/admin/v1/version-policy/ios',
      ...as(ops),
      payload: {
        minSupportedBuild: 50,
        latestBuild: 60,
        storeUrl: 'https://apps.apple.com/app/id1',
        reason: 'release 1.1',
      },
    });
    expect(policy.statusCode).toBe(200);
    const boot = await h.inject({ method: 'GET', url: '/v1/bootstrap', headers: { 'x-build': '40' } });
    expect(boot.json().version.status).toBe('update_required');
    const invalid = await h.inject({
      method: 'PUT',
      url: '/admin/v1/version-policy/ios',
      ...as(ops),
      payload: {
        minSupportedBuild: 70,
        latestBuild: 60,
        storeUrl: 'https://apps.apple.com/app/id1',
        reason: 'bad',
      },
    });
    expect(invalid.statusCode).toBe(400);

    const created = await h.inject({
      method: 'POST',
      url: '/admin/v1/maintenance',
      ...as(ops),
      payload: { scope: 'full', message: { en: 'Back soon' }, reason: 'database upgrade' },
    });
    expect(created.statusCode).toBe(201);
    const login = await h.inject({
      method: 'POST',
      url: '/v1/auth/login',
      payload: { email: 'x@example.com', password: 'whatever12345' },
    });
    expect(login.json().code).toBe('platform.maintenance');
    const cancel = await h.inject({
      method: 'POST',
      url: `/admin/v1/maintenance/${created.json().id}/cancel`,
      ...as(ops),
      payload: { reason: 'done early' },
    });
    expect(cancel.statusCode).toBe(200);
    const after = await h.inject({
      method: 'POST',
      url: '/v1/auth/login',
      payload: { email: 'x@example.com', password: 'whatever12345' },
    });
    expect(after.json().code).toBe('auth.invalid_credentials');

    // Live Ops may edit config but not version policy; Server Ops the reverse.
    expect(
      (
        await h.inject({
          method: 'PUT',
          url: '/admin/v1/version-policy/ios',
          ...as(live),
          payload: {
            minSupportedBuild: 1,
            latestBuild: 1,
            storeUrl: 'https://apps.apple.com/app/id1',
            reason: 'x x',
          },
        })
      ).statusCode,
    ).toBe(403);
    expect(
      (
        await h.inject({
          method: 'PUT',
          url: '/admin/v1/config/feature.voiceChat',
          ...as(ops),
          payload: { value: true, scope: 'client', reason: 'try' },
        })
      ).statusCode,
    ).toBe(403);

    const put = await h.inject({
      method: 'PUT',
      url: '/admin/v1/config/feature.voiceChat',
      ...as(live),
      payload: { value: true, scope: 'client', reason: 'beta test' },
    });
    expect(put.json()).toMatchObject({ key: 'feature.voiceChat', value: true, version: 1 });
    const put2 = await h.inject({
      method: 'PUT',
      url: '/admin/v1/config/feature.voiceChat',
      ...as(live),
      payload: { value: false, scope: 'client', reason: 'rollback' },
    });
    expect(put2.json().version).toBe(2);
    expect((await h.inject({ method: 'GET', url: '/v1/bootstrap' })).json().config.values).toEqual({
      'feature.voiceChat': false,
    });
    const badKey = await h.inject({
      method: 'PUT',
      url: '/admin/v1/config/NoDots',
      ...as(live),
      payload: { value: 1, scope: 'client', reason: 'x x x' },
    });
    expect(badKey.statusCode).toBe(400);
    const del = await h.inject({
      method: 'DELETE',
      url: '/admin/v1/config/feature.voiceChat',
      ...as(live),
      payload: { reason: 'cleanup' },
    });
    expect(del.statusCode).toBe(204);
    expect((await h.inject({ method: 'GET', url: '/v1/bootstrap' })).json().config.values).toEqual({});

    const trail = await h.db.sql<{ action: string }[]>`
      SELECT action FROM audit_log WHERE action IN ('version_policy.update','maintenance.create','maintenance.cancel','config.update','config.delete') ORDER BY occurred_at`;
    expect(trail.map((t) => t.action)).toEqual([
      'version_policy.update',
      'maintenance.create',
      'maintenance.cancel',
      'config.update',
      'config.update',
      'config.delete',
    ]);
  });
});
