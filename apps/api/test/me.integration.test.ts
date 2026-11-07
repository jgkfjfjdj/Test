import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { AccountService } from '../src/modules/accounts/service.js';
import { createHarness, type Harness } from './support/harness.js';
import { bearer, registerPlayer } from './support/players.js';

let h: Harness;
beforeAll(async () => {
  h = await createHarness();
});
afterAll(async () => {
  await h.close();
});

describe('profile', () => {
  it('renames, enforcing rules and uniqueness', async () => {
    const a = await registerPlayer(h);
    const b = await registerPlayer(h);
    const ok = await h.inject({
      method: 'PATCH',
      url: '/v1/me',
      headers: bearer(a.accessToken),
      payload: { displayName: 'New_Name' },
    });
    expect(ok.statusCode).toBe(200);
    expect(ok.json().displayName).toBe('New_Name');
    const taken = await h.inject({
      method: 'PATCH',
      url: '/v1/me',
      headers: bearer(b.accessToken),
      payload: { displayName: 'NEW_NAME' },
    });
    expect(taken.json().code).toBe('account.display_name_taken');
    const bad = await h.inject({
      method: 'PATCH',
      url: '/v1/me',
      headers: bearer(b.accessToken),
      payload: { displayName: 'moderator' },
    });
    expect(bad.json().code).toBe('account.display_name_invalid');
  });
});

describe('cloud saves', () => {
  it('creates, updates with versions, and reports conflicts with the current copy', async () => {
    const p = await registerPlayer(h);
    const auth = bearer(p.accessToken);
    expect(
      (await h.inject({ method: 'GET', url: '/v1/me/cloud-saves/hud', headers: auth })).json().code,
    ).toBe('cloud_save.not_found');

    const v1 = await h.inject({
      method: 'PUT',
      url: '/v1/me/cloud-saves/hud',
      headers: auth,
      payload: { baseVersion: 0, data: { joystick: [0.1, 0.2] } },
    });
    expect(v1.json()).toMatchObject({ slot: 'hud', version: 1, data: { joystick: [0.1, 0.2] } });

    const v2 = await h.inject({
      method: 'PUT',
      url: '/v1/me/cloud-saves/hud',
      headers: auth,
      payload: { baseVersion: 1, data: { joystick: [0.3, 0.2] } },
    });
    expect(v2.json().version).toBe(2);

    const stale = await h.inject({
      method: 'PUT',
      url: '/v1/me/cloud-saves/hud',
      headers: auth,
      payload: { baseVersion: 1, data: { joystick: [9, 9] } },
    });
    expect(stale.statusCode).toBe(409);
    expect(stale.json()).toMatchObject({
      code: 'cloud_save.version_conflict',
      current: { version: 2, data: { joystick: [0.3, 0.2] } },
    });

    const createTwice = await h.inject({
      method: 'PUT',
      url: '/v1/me/cloud-saves/hud',
      headers: auth,
      payload: { baseVersion: 0, data: {} },
    });
    expect(createTwice.statusCode).toBe(409);

    const read = await h.inject({ method: 'GET', url: '/v1/me/cloud-saves/hud', headers: auth });
    expect(read.json()).toMatchObject({ version: 2, data: { joystick: [0.3, 0.2] } });
  });

  it('enforces slot names, size limit and per-account isolation', async () => {
    const a = await registerPlayer(h);
    const b = await registerPlayer(h);
    const badSlot = await h.inject({
      method: 'PUT',
      url: '/v1/me/cloud-saves/inventory',
      headers: bearer(a.accessToken),
      payload: { baseVersion: 0, data: {} },
    });
    expect(badSlot.statusCode).toBe(400);
    const big = await h.inject({
      method: 'PUT',
      url: '/v1/me/cloud-saves/settings',
      headers: bearer(a.accessToken),
      payload: { baseVersion: 0, data: { blob: 'x'.repeat(70_000) } },
    });
    expect(big.statusCode).toBe(413);
    expect(big.json().code).toBe('cloud_save.too_large');
    await h.inject({
      method: 'PUT',
      url: '/v1/me/cloud-saves/settings',
      headers: bearer(a.accessToken),
      payload: { baseVersion: 0, data: { fov: 80 } },
    });
    const other = await h.inject({
      method: 'GET',
      url: '/v1/me/cloud-saves/settings',
      headers: bearer(b.accessToken),
    });
    expect(other.statusCode).toBe(404);
  });
});

describe('account deletion', () => {
  it('requires explicit confirmation', async () => {
    const p = await registerPlayer(h);
    const res = await h.inject({
      method: 'DELETE',
      url: '/v1/me',
      headers: bearer(p.accessToken),
      payload: { confirm: 'yes' },
    });
    expect(res.statusCode).toBe(400);
  });

  it('signs out everywhere, allows cancelling after signing in again, and purges after the grace period', async () => {
    const p = await registerPlayer(h);
    await h.inject({
      method: 'PUT',
      url: '/v1/me/cloud-saves/settings',
      headers: bearer(p.accessToken),
      payload: { baseVersion: 0, data: { a: 1 } },
    });

    const del = await h.inject({
      method: 'DELETE',
      url: '/v1/me',
      headers: bearer(p.accessToken),
      payload: { confirm: 'DELETE' },
    });
    expect(del.statusCode).toBe(202);
    expect(del.json().status).toBe('pending_deletion');
    expect(
      (await h.inject({ method: 'GET', url: '/v1/me', headers: bearer(p.accessToken) })).statusCode,
    ).toBe(401);
    expect(
      (await h.inject({ method: 'POST', url: '/v1/auth/refresh', payload: { refreshToken: p.refreshToken } }))
        .statusCode,
    ).toBe(401);

    const login = await h.inject({
      method: 'POST',
      url: '/v1/auth/login',
      payload: { email: p.email, password: p.password },
    });
    expect(login.json().account.status).toBe('pending_deletion');
    const token = login.json().accessToken;
    const me = await h.inject({ method: 'GET', url: '/v1/me', headers: bearer(token) });
    expect(me.json().deletion.purgeAfter).toBeTruthy();
    const cancel = await h.inject({ method: 'POST', url: '/v1/me/deletion/cancel', headers: bearer(token) });
    expect(cancel.json().status).toBe('active');
    const cancelAgain = await h.inject({
      method: 'POST',
      url: '/v1/me/deletion/cancel',
      headers: bearer(token),
    });
    expect(cancelAgain.json().code).toBe('account.deletion_not_pending');

    // Request again; purge does nothing before the grace period ends, then removes everything.
    await h.inject({
      method: 'DELETE',
      url: '/v1/me',
      headers: bearer(token),
      payload: { confirm: 'DELETE' },
    });
    const service = new AccountService(h.app.ctx);
    expect(await service.purgeExpiredDeletions()).toBe(0);
    h.clock.advance(8 * 86_400_000);
    try {
      expect(await service.purgeExpiredDeletions()).toBe(1);
    } finally {
      h.clock.advance(-8 * 86_400_000);
    }
    const left = await h.db.sql<{ n: number }[]>`
      SELECT (SELECT count(*) FROM accounts WHERE id = ${p.accountId})
           + (SELECT count(*) FROM cloud_saves WHERE account_id = ${p.accountId})
           + (SELECT count(*) FROM refresh_tokens WHERE account_id = ${p.accountId})
           + (SELECT count(*) FROM profiles WHERE account_id = ${p.accountId}) AS n`;
    expect(Number(left[0]?.n)).toBe(0);
    const gone = await h.inject({
      method: 'POST',
      url: '/v1/auth/login',
      payload: { email: p.email, password: p.password },
    });
    expect(gone.statusCode).toBe(401);
  });
});
