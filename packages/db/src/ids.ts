import { randomBytes } from 'node:crypto';

/**
 * UUIDv7 (RFC 9562): 48-bit Unix-ms timestamp + random bits. Time-ordered, so B-tree inserts stay
 * append-mostly. Monotonic within a process even when several ids are created in the same millisecond.
 */
let lastMs = 0;
let seq = 0;

export function newId(now: number = Date.now()): string {
  const bytes = randomBytes(16);
  let ms = now;
  if (ms <= lastMs) {
    ms = lastMs;
    seq = (seq + 1) & 0xfff;
    if (seq === 0) ms = ++lastMs; // 4096 ids in one ms: borrow the next millisecond
  } else {
    seq = bytes.readUInt16BE(6) & 0xfff;
  }
  lastMs = ms;

  bytes.writeUIntBE(ms, 0, 6);
  bytes[6] = 0x70 | (seq >> 8); // version 7 + high bits of the sequence
  bytes[7] = seq & 0xff;
  bytes[8] = ((bytes[8] ?? 0) & 0x3f) | 0x80; // RFC 4122 variant
  const hex = bytes.toString('hex');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export function isUuid(value: string): boolean {
  return UUID_RE.test(value);
}
