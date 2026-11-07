import { describe, expect, it } from 'vitest';
import { changedKeys } from '../features/audit/audit-page';
import { parseJsonValue } from '../features/liveops/config-page';
import { windowState } from '../features/liveops/maintenance-queries';
import { formatDuration, formatRelative } from './format';

describe('helpers', () => {
  it('parses remote-config JSON values and explains mistakes', () => {
    expect(parseJsonValue('true')).toEqual({ ok: true, value: true });
    expect(parseJsonValue('{"a": [1, 2]}')).toEqual({ ok: true, value: { a: [1, 2] } });
    expect(parseJsonValue('')).toMatchObject({ ok: false });
    expect(parseJsonValue('{a: 1}')).toMatchObject({
      ok: false,
      error: expect.stringMatching(/^Not valid JSON/),
    });
  });

  it('finds changed top-level keys in audit diffs', () => {
    expect(changedKeys({ roles: ['a'], name: 'x' }, { roles: ['a', 'b'], name: 'x' })).toEqual(['roles']);
    expect(changedKeys(null, { a: 1 })).toEqual([]);
  });

  it('derives maintenance window state', () => {
    const now = Date.parse('2027-01-01T12:00:00Z');
    const base = { id: '1', scope: 'full' as const, message: { en: 'x' }, cancelledAt: null };
    expect(windowState({ ...base, startsAt: '2027-01-01T11:00:00Z', endsAt: null }, now)).toBe('active');
    expect(windowState({ ...base, startsAt: '2027-01-01T13:00:00Z', endsAt: null }, now)).toBe('scheduled');
    expect(
      windowState({ ...base, startsAt: '2027-01-01T10:00:00Z', endsAt: '2027-01-01T11:00:00Z' }, now),
    ).toBe('ended');
    expect(
      windowState(
        { ...base, startsAt: '2027-01-01T11:00:00Z', endsAt: null, cancelledAt: '2027-01-01T11:30:00Z' },
        now,
      ),
    ).toBe('cancelled');
  });

  it('formats durations and relative times', () => {
    expect(formatDuration(90)).toBe('1m');
    expect(formatDuration(3 * 3600 + 120)).toBe('3h 2m');
    expect(formatDuration(2 * 86400 + 3600)).toBe('2d 1h');
    const now = Date.parse('2027-01-01T12:00:00Z');
    expect(formatRelative('2027-01-01T11:59:50Z', now)).toBe('just now');
    expect(formatRelative(null)).toBe('never');
    expect(formatRelative('2027-01-01T09:00:00Z', now)).toMatch(/3 hours ago/);
  });
});
