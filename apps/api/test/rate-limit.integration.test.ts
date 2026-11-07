import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { createHarness, type Harness } from './support/harness.js';

let h: Harness;
beforeAll(async () => {
  h = await createHarness({ RATE_LIMIT_MULTIPLIER: '1' });
});
afterAll(async () => {
  await h.close();
});

describe('rate limiting (Redis-backed)', () => {
  it('limits registrations per IP with a problem+json 429 and Retry-After', async () => {
    const statuses: number[] = [];
    let last;
    for (let i = 0; i < 6; i++) {
      last = await h.inject({
        method: 'POST',
        url: '/v1/auth/register',
        remoteAddress: '203.0.113.7',
        payload: {
          email: `rl${i}@example.com`,
          password: 'a long and decent passphrase',
          displayName: `RateTest${i}`,
        },
      });
      statuses.push(last.statusCode);
    }
    expect(statuses.slice(0, 5)).toEqual([201, 201, 201, 201, 201]);
    expect(statuses[5]).toBe(429);
    expect(last!.headers['content-type']).toContain('application/problem+json');
    expect(last!.json().code).toBe('common.rate_limited');
    expect(Number(last!.headers['retry-after'])).toBeGreaterThan(0);

    // A different IP is unaffected.
    const other = await h.inject({
      method: 'POST',
      url: '/v1/auth/register',
      remoteAddress: '198.51.100.9',
      payload: {
        email: 'other-ip@example.com',
        password: 'a long and decent passphrase',
        displayName: 'OtherIp',
      },
    });
    expect(other.statusCode).toBe(201);
  });
});
