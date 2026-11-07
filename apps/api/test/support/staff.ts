import { expect } from 'vitest';
import { totpAt, base32Decode } from '../../src/lib/totp.js';
import { StaffService } from '../../src/modules/staff/service.js';
import type { Harness } from './harness.js';

export interface TestStaff {
  id: string;
  email: string;
  password: string;
  secret: Buffer;
  cookie: string;
  csrf: string;
}

let n = 0;

/** A fresh authenticator code: moves the test clock to the next 30-second step first (codes are single-use). */
export function nextCode(h: Harness, secret: Buffer): string {
  h.clock.advance(30_000);
  return totpAt(secret, Math.floor(h.clock.now().getTime() / 1000));
}

export function staffHeaders(s: Pick<TestStaff, 'csrf'>): Record<string, string> {
  return { 'x-csrf-token': s.csrf };
}

export function staffCookies(s: Pick<TestStaff, 'cookie'>): Record<string, string> {
  return { kindling_staff: s.cookie };
}

/** Invites, sets up (password + TOTP) and signs in a staff user with the given roles. */
export async function createStaff(h: Harness, roles: string[]): Promise<TestStaff> {
  n += 1;
  const email = `staff${n}_${Date.now()}@studio.example`;
  const password = 'a long staff passphrase 42';
  const service = new StaffService(h.app.ctx);
  const { id, setupToken } = await service.invite({ email, name: `Staff ${n}`, roles });
  const begin = await h.inject({
    method: 'POST',
    url: '/admin/v1/auth/setup/begin',
    payload: { token: setupToken },
  });
  expect(begin.statusCode, begin.body).toBe(200);
  const secret = base32Decode(begin.json().secret);
  const complete = await h.inject({
    method: 'POST',
    url: '/admin/v1/auth/setup/complete',
    payload: { token: setupToken, password, totp: nextCode(h, secret) },
  });
  expect(complete.statusCode, complete.body).toBe(200);
  const login = await h.inject({
    method: 'POST',
    url: '/admin/v1/auth/login',
    payload: { email, password, totp: nextCode(h, secret) },
  });
  expect(login.statusCode, login.body).toBe(200);
  const cookie = login.cookies.find((c) => c.name === 'kindling_staff');
  return { id, email, password, secret, cookie: cookie!.value, csrf: login.json().csrfToken };
}
