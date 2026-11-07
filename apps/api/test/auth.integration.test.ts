import { accounts } from '@kindling/db';
import { eq } from 'drizzle-orm';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { createHarness, tokenFromMail, type Harness } from './support/harness.js';
import { bearer, registerPlayer } from './support/players.js';

let h: Harness;
beforeAll(async () => {
  h = await createHarness({}, { breachChecker: { isBreached: async (p) => p === 'password12345' } });
});
afterAll(async () => {
  await h.close();
});

describe('registration', () => {
  it('creates an account, signs in, and sends a verification email', async () => {
    const res = await h.inject({
      method: 'POST',
      url: '/v1/auth/register',
      payload: {
        email: 'Ada@Example.com',
        password: 'a long and decent passphrase',
        displayName: 'Ada_Lovelace',
      },
    });
    expect(res.statusCode).toBe(201);
    const body = res.json();
    expect(body.account).toMatchObject({
      displayName: 'Ada_Lovelace',
      email: 'Ada@Example.com',
      emailVerified: false,
      status: 'active',
    });
    expect(body.created).toBe(true);
    expect(body.refreshToken).toMatch(/^[A-Za-z0-9_-]{43}$/);
    expect(h.mailer.lastTo('ada@example.com')?.text).toContain(
      'https://api.test.invalid/account/verify-email?token=',
    );

    const me = await h.inject({ method: 'GET', url: '/v1/me', headers: bearer(body.accessToken) });
    expect(me.statusCode).toBe(200);
    expect(me.json()).toMatchObject({ id: body.account.id, profile: { level: 1, xp: 0 } });
  });

  it('rejects duplicate emails (case-insensitive) and display names (case-insensitive)', async () => {
    const p = await registerPlayer(h, { displayName: 'Dupe_Name' });
    const sameEmail = await h.inject({
      method: 'POST',
      url: '/v1/auth/register',
      payload: {
        email: p.email.toUpperCase(),
        password: 'a long and decent passphrase',
        displayName: 'Other_Name',
      },
    });
    expect(sameEmail.statusCode).toBe(409);
    expect(sameEmail.json().code).toBe('auth.email_taken');
    const sameName = await h.inject({
      method: 'POST',
      url: '/v1/auth/register',
      payload: {
        email: 'new@example.com',
        password: 'a long and decent passphrase',
        displayName: 'dupe_name',
      },
    });
    expect(sameName.statusCode).toBe(409);
    expect(sameName.json().code).toBe('account.display_name_taken');
  });

  it.each([
    ['x', 'format'],
    ['has space', 'format'],
    ['12345', 'format'],
    ['Admin', 'reserved'],
    ['KindlingDev', 'reserved'],
  ])('rejects display name %s (%s)', async (displayName, reason) => {
    const res = await h.inject({
      method: 'POST',
      url: '/v1/auth/register',
      payload: {
        email: `${reason}${Math.random()}@example.com`,
        password: 'a long and decent passphrase',
        displayName,
      },
    });
    expect(res.statusCode).toBe(400);
    expect(res.json()).toMatchObject({ code: 'account.display_name_invalid', reason });
  });

  it('rejects weak, identity-based and breached passwords, and bad emails', async () => {
    const base = { email: 'pw@example.com', displayName: 'PwTester' };
    const short = await h.inject({
      method: 'POST',
      url: '/v1/auth/register',
      payload: { ...base, password: 'short' },
    });
    expect(short.json()).toMatchObject({ code: 'auth.password_weak', reason: 'too_short' });
    const breached = await h.inject({
      method: 'POST',
      url: '/v1/auth/register',
      payload: { ...base, password: 'password12345' },
    });
    expect(breached.json()).toMatchObject({ code: 'auth.password_breached' });
    const mine = await h.inject({
      method: 'POST',
      url: '/v1/auth/register',
      payload: { ...base, password: 'pwtester-is-me-123' },
    });
    expect(mine.json()).toMatchObject({ code: 'auth.password_weak', reason: 'contains_identity' });
    const email = await h.inject({
      method: 'POST',
      url: '/v1/auth/register',
      payload: { ...base, email: 'nope', password: 'a long passphrase ok' },
    });
    expect(email.json().code).toBe('auth.email_invalid');
    const extra = await h.inject({
      method: 'POST',
      url: '/v1/auth/register',
      payload: { ...base, password: 'a long passphrase ok', admin: true },
    });
    expect(extra.json().code).toBe('common.validation_failed');
  });
});

describe('email verification', () => {
  it('verifies once via the API and rejects reuse', async () => {
    const p = await registerPlayer(h);
    const token = tokenFromMail(h.mailer, p.email, '/account/verify-email');
    const ok = await h.inject({ method: 'POST', url: '/v1/auth/verify-email', payload: { token } });
    expect(ok.statusCode).toBe(200);
    const me = await h.inject({ method: 'GET', url: '/v1/me', headers: bearer(p.accessToken) });
    expect(me.json().emailVerified).toBe(true);
    const again = await h.inject({ method: 'POST', url: '/v1/auth/verify-email', payload: { token } });
    expect(again.statusCode).toBe(400);
    expect(again.json().code).toBe('auth.token_invalid');
  });

  it('verifies through the emailed link page and resends on request', async () => {
    const p = await registerPlayer(h);
    const resend = await h.inject({
      method: 'POST',
      url: '/v1/auth/verify-email/resend',
      headers: bearer(p.accessToken),
    });
    expect(resend.json().status).toBe('sent');
    const token = tokenFromMail(h.mailer, p.email, '/account/verify-email');
    const page = await h.inject({ method: 'GET', url: `/account/verify-email?token=${token}` });
    expect(page.statusCode).toBe(200);
    expect(page.headers['content-type']).toContain('text/html');
    expect(page.headers['content-security-policy']).toContain("default-src 'none'");
    expect(page.body).toContain('Email confirmed');
    const done = await h.inject({
      method: 'POST',
      url: '/v1/auth/verify-email/resend',
      headers: bearer(p.accessToken),
    });
    expect(done.json().status).toBe('already_verified');
    const bad = await h.inject({
      method: 'GET',
      url: '/account/verify-email?token=<script>alert(1)</script>',
    });
    expect(bad.statusCode).toBe(400);
    expect(bad.body).not.toContain('<script>');
  });

  it('expires verification links after 48 hours', async () => {
    const p = await registerPlayer(h);
    const token = tokenFromMail(h.mailer, p.email, '/account/verify-email');
    h.clock.advance(49 * 3600_000);
    try {
      const res = await h.inject({ method: 'POST', url: '/v1/auth/verify-email', payload: { token } });
      expect(res.json().code).toBe('auth.token_invalid');
    } finally {
      h.clock.advance(-49 * 3600_000);
    }
  });
});

describe('login and sessions', () => {
  it('signs in with the right password and gives identical errors for wrong password and unknown email', async () => {
    const p = await registerPlayer(h);
    const ok = await h.inject({
      method: 'POST',
      url: '/v1/auth/login',
      payload: { email: p.email, password: p.password },
    });
    expect(ok.statusCode).toBe(200);
    expect(ok.json().created).toBe(false);
    const wrong = await h.inject({
      method: 'POST',
      url: '/v1/auth/login',
      payload: { email: p.email, password: 'not the password!' },
    });
    const unknown = await h.inject({
      method: 'POST',
      url: '/v1/auth/login',
      payload: { email: 'ghost@example.com', password: 'whatever12345' },
    });
    expect(wrong.statusCode).toBe(401);
    expect(unknown.statusCode).toBe(401);
    expect(wrong.json().code).toBe('auth.invalid_credentials');
    expect(unknown.json().code).toBe('auth.invalid_credentials');
    expect(wrong.json().detail).toBe(unknown.json().detail);
  });

  it('locks an email after 10 failed attempts', async () => {
    const p = await registerPlayer(h);
    for (let i = 0; i < 10; i++) {
      const res = await h.inject({
        method: 'POST',
        url: '/v1/auth/login',
        payload: { email: p.email, password: `wrong-${i}-password` },
      });
      expect(res.statusCode).toBe(401);
    }
    const locked = await h.inject({
      method: 'POST',
      url: '/v1/auth/login',
      payload: { email: p.email, password: p.password },
    });
    expect(locked.statusCode).toBe(429);
    expect(locked.json().code).toBe('auth.too_many_attempts');
  });

  it('rejects requests without, or with invalid, access tokens', async () => {
    const none = await h.inject({ method: 'GET', url: '/v1/me' });
    expect(none.statusCode).toBe(401);
    expect(none.json().code).toBe('auth.unauthorized');
    const junk = await h.inject({ method: 'GET', url: '/v1/me', headers: bearer('abc.def.ghi') });
    expect(junk.statusCode).toBe(401);
    const foreign = await h.keys.tickets.sign(
      { scope: 'player', sid: 'x' },
      { subject: 'someone', expiresInSeconds: 60 },
    );
    const wrongKey = await h.inject({ method: 'GET', url: '/v1/me', headers: bearer(foreign) });
    expect(wrongKey.statusCode).toBe(401);
  });

  it('expires access tokens after 15 minutes', async () => {
    const p = await registerPlayer(h);
    h.clock.advance(16 * 60_000);
    try {
      const res = await h.inject({ method: 'GET', url: '/v1/me', headers: bearer(p.accessToken) });
      expect(res.statusCode).toBe(401);
    } finally {
      h.clock.advance(-16 * 60_000);
    }
  });

  it('rotates refresh tokens and ends the session when an old token is reused', async () => {
    const p = await registerPlayer(h);
    const first = await h.inject({
      method: 'POST',
      url: '/v1/auth/refresh',
      payload: { refreshToken: p.refreshToken },
    });
    expect(first.statusCode).toBe(200);
    const rotated = first.json();
    expect(rotated.refreshToken).not.toBe(p.refreshToken);
    expect(
      (await h.inject({ method: 'GET', url: '/v1/me', headers: bearer(rotated.accessToken) })).statusCode,
    ).toBe(200);

    const reuse = await h.inject({
      method: 'POST',
      url: '/v1/auth/refresh',
      payload: { refreshToken: p.refreshToken },
    });
    expect(reuse.statusCode).toBe(401);
    expect(reuse.json().code).toBe('auth.refresh_token_reused');

    // The whole family is dead: the newest refresh token and its access token stop working.
    const next = await h.inject({
      method: 'POST',
      url: '/v1/auth/refresh',
      payload: { refreshToken: rotated.refreshToken },
    });
    expect(next.statusCode).toBe(401);
    expect(
      (await h.inject({ method: 'GET', url: '/v1/me', headers: bearer(rotated.accessToken) })).statusCode,
    ).toBe(401);
  });

  it('logout ends only that session and is idempotent', async () => {
    const p = await registerPlayer(h);
    const other = await h.inject({
      method: 'POST',
      url: '/v1/auth/login',
      payload: { email: p.email, password: p.password },
    });
    const out = await h.inject({
      method: 'POST',
      url: '/v1/auth/logout',
      payload: { refreshToken: p.refreshToken },
    });
    expect(out.statusCode).toBe(204);
    expect(
      (await h.inject({ method: 'POST', url: '/v1/auth/logout', payload: { refreshToken: p.refreshToken } }))
        .statusCode,
    ).toBe(204);
    expect(
      (await h.inject({ method: 'GET', url: '/v1/me', headers: bearer(p.accessToken) })).statusCode,
    ).toBe(401);
    expect(
      (await h.inject({ method: 'POST', url: '/v1/auth/refresh', payload: { refreshToken: p.refreshToken } }))
        .statusCode,
    ).toBe(401);
    expect(
      (await h.inject({ method: 'GET', url: '/v1/me', headers: bearer(other.json().accessToken) }))
        .statusCode,
    ).toBe(200);
  });

  it('refuses suspended accounts', async () => {
    const p = await registerPlayer(h);
    await h.db.db.update(accounts).set({ status: 'suspended' }).where(eq(accounts.id, p.accountId));
    const res = await h.inject({
      method: 'POST',
      url: '/v1/auth/login',
      payload: { email: p.email, password: p.password },
    });
    expect(res.statusCode).toBe(403);
    expect(res.json().code).toBe('auth.account_suspended');
    const refresh = await h.inject({
      method: 'POST',
      url: '/v1/auth/refresh',
      payload: { refreshToken: p.refreshToken },
    });
    expect(refresh.statusCode).toBe(403);
  });
});

describe('password reset', () => {
  it('never reveals whether an email exists', async () => {
    const before = h.mailer.sent.length;
    const res = await h.inject({
      method: 'POST',
      url: '/v1/auth/password/forgot',
      payload: { email: 'nobody@example.com' },
    });
    expect(res.statusCode).toBe(202);
    expect(h.mailer.sent.length).toBe(before);
  });

  it('resets the password, verifies the email, and signs out everywhere', async () => {
    const p = await registerPlayer(h);
    await h.inject({ method: 'POST', url: '/v1/auth/password/forgot', payload: { email: p.email } });
    const token = tokenFromMail(h.mailer, p.email, '/account/reset-password');

    const weak = await h.inject({
      method: 'POST',
      url: '/v1/auth/password/reset',
      payload: { token, newPassword: 'short' },
    });
    expect(weak.json().code).toBe('auth.password_weak');

    const ok = await h.inject({
      method: 'POST',
      url: '/v1/auth/password/reset',
      payload: { token, newPassword: 'a brand new passphrase' },
    });
    expect(ok.statusCode).toBe(200); // the weak attempt did not burn the token
    expect(
      (await h.inject({ method: 'GET', url: '/v1/me', headers: bearer(p.accessToken) })).statusCode,
    ).toBe(401);
    expect(
      (await h.inject({ method: 'POST', url: '/v1/auth/refresh', payload: { refreshToken: p.refreshToken } }))
        .statusCode,
    ).toBe(401);

    const oldPw = await h.inject({
      method: 'POST',
      url: '/v1/auth/login',
      payload: { email: p.email, password: p.password },
    });
    expect(oldPw.statusCode).toBe(401);
    const newPw = await h.inject({
      method: 'POST',
      url: '/v1/auth/login',
      payload: { email: p.email, password: 'a brand new passphrase' },
    });
    expect(newPw.statusCode).toBe(200);
    expect(newPw.json().account.emailVerified).toBe(true);

    const reuse = await h.inject({
      method: 'POST',
      url: '/v1/auth/password/reset',
      payload: { token, newPassword: 'another passphrase 9' },
    });
    expect(reuse.json().code).toBe('auth.token_invalid');
  });

  it('works through the emailed HTML form', async () => {
    const p = await registerPlayer(h);
    await h.inject({ method: 'POST', url: '/v1/auth/password/forgot', payload: { email: p.email } });
    const token = tokenFromMail(h.mailer, p.email, '/account/reset-password');
    const form = await h.inject({ method: 'GET', url: `/account/reset-password?token=${token}` });
    expect(form.body).toContain('<form method="post" action="/account/reset-password">');
    const mismatch = await h.inject({
      method: 'POST',
      url: '/account/reset-password',
      headers: { 'content-type': 'application/x-www-form-urlencoded' },
      payload: new URLSearchParams({
        token,
        password: 'form passphrase 1',
        confirm: 'form passphrase 2',
      }).toString(),
    });
    expect(mismatch.body).toContain('Passwords differ');
    const done = await h.inject({
      method: 'POST',
      url: '/account/reset-password',
      headers: { 'content-type': 'application/x-www-form-urlencoded' },
      payload: new URLSearchParams({
        token,
        password: 'form passphrase 1',
        confirm: 'form passphrase 1',
      }).toString(),
    });
    expect(done.statusCode).toBe(200);
    expect(done.body).toContain('Password changed');
  });
});

describe('Sign in with Apple', () => {
  const nonce = 'nonce-0123456789abcdef';

  it('asks new players for a display name without creating anything', async () => {
    const identityToken = await h.apple.token({ sub: 'apple-new-1', rawNonce: nonce });
    const res = await h.inject({ method: 'POST', url: '/v1/auth/apple', payload: { identityToken, nonce } });
    expect(res.statusCode).toBe(422);
    expect(res.json().code).toBe('account.display_name_required');
    const count = await h.db.sql<
      { n: number }[]
    >`SELECT count(*)::int AS n FROM auth_identities WHERE subject = 'apple-new-1'`;
    expect(count[0]?.n).toBe(0);
  });

  it('creates the account on first sign-in and signs the same account in afterwards', async () => {
    const identityToken = await h.apple.token({
      sub: 'apple-2',
      rawNonce: nonce,
      email: 'relay123@privaterelay.appleid.com',
    });
    const created = await h.inject({
      method: 'POST',
      url: '/v1/auth/apple',
      payload: { identityToken, nonce, displayName: 'Apple_Fan' },
    });
    expect(created.statusCode).toBe(201);
    expect(created.json().account).toMatchObject({
      displayName: 'Apple_Fan',
      email: 'relay123@privaterelay.appleid.com',
      emailVerified: true,
    });

    const again = await h.inject({
      method: 'POST',
      url: '/v1/auth/apple',
      payload: { identityToken: await h.apple.token({ sub: 'apple-2', rawNonce: nonce }), nonce },
    });
    expect(again.statusCode).toBe(200);
    expect(again.json().account.id).toBe(created.json().account.id);
  });

  it('does not attach an email already used by another account', async () => {
    const p = await registerPlayer(h);
    const identityToken = await h.apple.token({ sub: 'apple-3', rawNonce: nonce, email: p.email });
    const res = await h.inject({
      method: 'POST',
      url: '/v1/auth/apple',
      payload: { identityToken, nonce, displayName: 'Apple_Three' },
    });
    expect(res.statusCode).toBe(201);
    expect(res.json().account.email).toBeNull();
    expect(res.json().account.id).not.toBe(p.accountId);
  });

  it('rejects invalid identity tokens', async () => {
    const identityToken = await h.apple.token({ sub: 'apple-4', rawNonce: 'a-different-nonce-123' });
    const res = await h.inject({
      method: 'POST',
      url: '/v1/auth/apple',
      payload: { identityToken, nonce, displayName: 'Apple_Four' },
    });
    expect(res.statusCode).toBe(401);
    expect(res.json().code).toBe('auth.apple_token_invalid');
  });
});
