import { createHmac, randomBytes } from 'node:crypto';

/** RFC 6238 TOTP (HMAC-SHA1, 30-second steps, 6 digits) — compatible with standard authenticator apps. */
const STEP_SECONDS = 30;
const DIGITS = 6;

const BASE32 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';

export function base32Encode(data: Buffer): string {
  let bits = 0;
  let value = 0;
  let out = '';
  for (const byte of data) {
    value = (value << 8) | byte;
    bits += 8;
    while (bits >= 5) {
      out += BASE32[(value >>> (bits - 5)) & 31];
      bits -= 5;
    }
  }
  if (bits > 0) out += BASE32[(value << (5 - bits)) & 31];
  return out;
}

export function base32Decode(text: string): Buffer {
  const clean = text.replace(/=+$/g, '').replace(/\s+/g, '').toUpperCase();
  let bits = 0;
  let value = 0;
  const out: number[] = [];
  for (const char of clean) {
    const index = BASE32.indexOf(char);
    if (index < 0) throw new Error('invalid base32');
    value = (value << 5) | index;
    bits += 5;
    if (bits >= 8) {
      out.push((value >>> (bits - 8)) & 0xff);
      bits -= 8;
    }
  }
  return Buffer.from(out);
}

export function generateTotpSecret(): Buffer {
  return randomBytes(20); // 160 bits, as recommended by RFC 4226
}

/** HOTP value for a counter (RFC 4226). */
export function hotp(secret: Buffer, counter: number, digits = DIGITS): string {
  const message = Buffer.alloc(8);
  message.writeBigUInt64BE(BigInt(counter));
  const mac = createHmac('sha1', secret).update(message).digest();
  const offset = mac[mac.length - 1]! & 0x0f;
  const code = (mac.readUInt32BE(offset) & 0x7fffffff) % 10 ** digits;
  return code.toString().padStart(digits, '0');
}

export function totpAt(secret: Buffer, unixSeconds: number, digits = DIGITS): string {
  return hotp(secret, Math.floor(unixSeconds / STEP_SECONDS), digits);
}

/**
 * Checks a code against the current step ±1 (clock drift). Returns the matched step so callers
 * can reject reuse of the same step (replay protection).
 */
export function verifyTotp(secret: Buffer, code: string, unixSeconds: number): number | null {
  if (!/^\d{6}$/.test(code)) return null;
  const current = Math.floor(unixSeconds / STEP_SECONDS);
  for (const step of [current - 1, current, current + 1]) {
    if (hotp(secret, step) === code) return step;
  }
  return null;
}

export function otpauthUri(secret: Buffer, accountName: string, issuer = 'KINDLING Staff'): string {
  const label = encodeURIComponent(`${issuer}:${accountName}`);
  return `otpauth://totp/${label}?secret=${base32Encode(secret)}&issuer=${encodeURIComponent(issuer)}&algorithm=SHA1&digits=${DIGITS}&period=${STEP_SECONDS}`;
}
