import { createHmac } from 'node:crypto';
import { mkdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

export const ADMIN = { email: 'e2e-lead@studio.example', password: 'correct horse battery staple console' };

export function setupToken(): string {
  return (JSON.parse(readFileSync('e2e/.auth/state.json', 'utf8')) as { setupToken: string }).setupToken;
}

function base32Decode(text: string): Buffer {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = 0;
  let value = 0;
  const out: number[] = [];
  for (const char of text.replace(/\s+/g, '').toUpperCase()) {
    value = (value << 5) | alphabet.indexOf(char);
    bits += 5;
    if (bits >= 8) {
      out.push((value >>> (bits - 8)) & 0xff);
      bits -= 8;
    }
  }
  return Buffer.from(out);
}

/** TOTP code for the current 30-second step plus `offset` steps (the server accepts ±1). */
export function totp(secretBase32: string, offset = 0): string {
  const step = Math.floor(Date.now() / 30_000) + offset;
  const message = Buffer.alloc(8);
  message.writeBigUInt64BE(BigInt(step));
  const mac = createHmac('sha1', base32Decode(secretBase32)).update(message).digest();
  const o = mac[mac.length - 1]! & 0x0f;
  return String((mac.readUInt32BE(o) & 0x7fffffff) % 1_000_000).padStart(6, '0');
}

export function shot(name: string): string {
  const dir = process.env.E2E_SCREENSHOT_DIR ?? 'e2e/screenshots';
  mkdirSync(dir, { recursive: true });
  return join(dir, name);
}
