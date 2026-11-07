import { expect } from 'vitest';
import type { Harness } from './harness.js';

let counter = 0;

export interface TestPlayer {
  accountId: string;
  email: string;
  password: string;
  displayName: string;
  accessToken: string;
  refreshToken: string;
}

/** Registers a fresh player through the public API. */
export async function registerPlayer(
  h: Harness,
  overrides: Partial<{ email: string; displayName: string; password: string }> = {},
): Promise<TestPlayer> {
  counter += 1;
  const email = overrides.email ?? `player${counter}_${Date.now()}@example.com`;
  const displayName = overrides.displayName ?? `Survivor${counter}${Math.floor(Math.random() * 1000)}`;
  const password = overrides.password ?? 'a long and decent passphrase';
  const res = await h.inject({
    method: 'POST',
    url: '/v1/auth/register',
    payload: { email, password, displayName },
  });
  expect(res.statusCode, res.body).toBe(201);
  const body = res.json();
  return {
    accountId: body.account.id,
    email,
    password,
    displayName,
    accessToken: body.accessToken,
    refreshToken: body.refreshToken,
  };
}

export function bearer(token: string): Record<string, string> {
  return { authorization: `Bearer ${token}` };
}
