import { describe, expect, it } from 'vitest';
import { isUuid, newId } from '../src/ids.js';

describe('newId (UUIDv7)', () => {
  it('produces RFC 9562 version-7 UUIDs', () => {
    const id = newId();
    expect(isUuid(id)).toBe(true);
    expect(id[14]).toBe('7');
    expect(['8', '9', 'a', 'b']).toContain(id[19]);
  });

  it('embeds the timestamp in the first 48 bits', () => {
    const now = Date.UTC(2027, 0, 1);
    const id = newId(now + 10_000_000); // later than any id generated so far in this process
    const ms = parseInt(id.replace(/-/g, '').slice(0, 12), 16);
    expect(ms).toBe(now + 10_000_000);
  });

  it('is strictly increasing within one millisecond', () => {
    const t = Date.now() + 20_000_000;
    const ids = Array.from({ length: 5000 }, () => newId(t));
    const sorted = [...ids].sort();
    expect(sorted).toEqual(ids);
    expect(new Set(ids).size).toBe(ids.length);
  });
});
