import { describe, expect, it } from 'vitest';
import { AppleTokenError } from '../src/lib/apple.js';
import { constantTimeEqual, decryptSecret, encryptSecret, randomToken, sha256 } from '../src/lib/crypto.js';
import { generatePrivateJwk, KeyRing } from '../src/lib/keys.js';
import {
  checkPasswordPolicy,
  hashPassword,
  hibpBreachChecker,
  verifyAgainstDummy,
  verifyPassword,
} from '../src/lib/passwords.js';
import { base32Decode, base32Encode, hotp, otpauthUri, totpAt, verifyTotp } from '../src/lib/totp.js';
import { FakeApple } from './support/harness.js';

describe('TOTP / HOTP (RFC 4226 + RFC 6238 test vectors)', () => {
  const secret = Buffer.from('12345678901234567890', 'ascii');

  it('matches RFC 4226 HOTP values', () => {
    const expected = [
      '755224',
      '287082',
      '359152',
      '969429',
      '338314',
      '254676',
      '287922',
      '162583',
      '399871',
      '520489',
    ];
    expected.forEach((code, counter) => expect(hotp(secret, counter)).toBe(code));
  });

  it('matches RFC 6238 SHA-1 TOTP values (8 digits)', () => {
    const vectors: [number, string][] = [
      [59, '94287082'],
      [1111111109, '07081804'],
      [1111111111, '14050471'],
      [1234567890, '89005924'],
      [2000000000, '69279037'],
    ];
    for (const [time, code] of vectors) expect(totpAt(secret, time, 8)).toBe(code);
  });

  it('accepts the adjacent step for clock drift and rejects older codes', () => {
    const t = 1_700_000_000;
    expect(verifyTotp(secret, totpAt(secret, t), t)).toBe(Math.floor(t / 30));
    expect(verifyTotp(secret, totpAt(secret, t - 30), t)).toBe(Math.floor(t / 30) - 1);
    expect(verifyTotp(secret, totpAt(secret, t - 90), t)).toBeNull();
    expect(verifyTotp(secret, 'abcdef', t)).toBeNull();
  });

  it('round-trips base32 and builds an otpauth URI', () => {
    const data = Buffer.from('hello totp secret!!!');
    expect(base32Decode(base32Encode(data))).toEqual(data);
    expect(base32Encode(Buffer.from('foobar'))).toBe('MZXW6YTBOI');
    expect(otpauthUri(secret, 'ops@example.com')).toMatch(
      /^otpauth:\/\/totp\/KINDLING%20Staff%3Aops%40example\.com\?secret=GEZDGNBV/,
    );
  });
});

describe('crypto helpers', () => {
  it('random tokens are URL-safe and unique', () => {
    const a = randomToken();
    expect(a).toMatch(/^[A-Za-z0-9_-]{43}$/);
    expect(randomToken()).not.toBe(a);
  });

  it('AES-GCM round-trips and detects tampering', () => {
    const key = Buffer.alloc(32, 1);
    const blob = encryptSecret(key, Buffer.from('totp-secret'));
    expect(decryptSecret(key, blob).toString()).toBe('totp-secret');
    const tampered = Buffer.from(blob);
    tampered[tampered.length - 1]! ^= 1;
    expect(() => decryptSecret(key, tampered)).toThrow();
    expect(() => decryptSecret(Buffer.alloc(32, 2), blob)).toThrow();
  });

  it('constant-time compare handles different lengths', () => {
    expect(constantTimeEqual(sha256('a'), sha256('a'))).toBe(true);
    expect(constantTimeEqual(sha256('a'), sha256('b'))).toBe(false);
    expect(constantTimeEqual(Buffer.from('a'), Buffer.from('ab'))).toBe(false);
  });
});

describe('KeyRing (Ed25519 JWTs)', () => {
  it('signs and verifies with issuer, audience and expiry', async () => {
    const ring = await KeyRing.fromJwks('kindling-test', [await generatePrivateJwk('k1')]);
    const token = await ring.sign(
      { scope: 'player' },
      { subject: 'acc-1', audience: 'srv-1', expiresInSeconds: 60 },
    );
    const payload = await ring.verify(token, { audience: 'srv-1' });
    expect(payload).toMatchObject({ sub: 'acc-1', aud: 'srv-1', iss: 'kindling-test', scope: 'player' });
    await expect(ring.verify(token, { audience: 'srv-2' })).rejects.toThrow();
  });

  it('rejects expired, tampered and foreign tokens', async () => {
    const ring = await KeyRing.fromJwks('kindling-test', [await generatePrivateJwk('k1')]);
    const other = await KeyRing.fromJwks('kindling-test', [await generatePrivateJwk('k1')]);
    const expired = await ring.sign(
      {},
      { expiresInSeconds: 60, issuedAt: Math.floor(Date.now() / 1000) - 600 },
    );
    await expect(ring.verify(expired)).rejects.toThrow(/exp/);
    const valid = await ring.sign({ role: 'player' }, { expiresInSeconds: 60 });
    const [h, , s] = valid.split('.');
    const forgedBody = Buffer.from(JSON.stringify({ role: 'admin', iss: 'kindling-test' })).toString(
      'base64url',
    );
    await expect(ring.verify(`${h}.${forgedBody}.${s}`)).rejects.toThrow();
    await expect(other.verify(valid)).rejects.toThrow(); // same kid, different key
  });

  it('keeps verifying old tokens after rotation', async () => {
    const oldKey = await generatePrivateJwk('old');
    const before = await KeyRing.fromJwks('kindling-test', [oldKey]);
    const token = await before.sign({}, { expiresInSeconds: 60 });
    const after = await KeyRing.fromJwks('kindling-test', [await generatePrivateJwk('new'), oldKey]);
    await expect(after.verify(token)).resolves.toBeTruthy();
    expect(after.activeKid).toBe('new');
    expect(after.publicJwks().keys.map((k) => k.kid)).toEqual(['new', 'old']);
    expect(after.publicJwks().keys.every((k) => k.d === undefined)).toBe(true);
  });

  it('refuses non-Ed25519 or kid-less keys', async () => {
    const { kid: _kid, ...noKid } = await generatePrivateJwk('x');
    await expect(KeyRing.fromJwks('t', [noKid])).rejects.toThrow(/kid/);
    await expect(KeyRing.fromEnv('t', '[]')).rejects.toThrow(/at least one/);
  });
});

describe('passwords', () => {
  it('hashes with Argon2id and verifies', async () => {
    const hash = await hashPassword('correct horse battery');
    expect(hash.startsWith('$argon2id$')).toBe(true);
    expect(await verifyPassword(hash, 'correct horse battery')).toBe(true);
    expect(await verifyPassword(hash, 'wrong')).toBe(false);
    expect(await verifyPassword('not-a-hash', 'x')).toBe(false);
    expect(await verifyAgainstDummy('anything')).toBe(false);
  });

  it('enforces the local policy', () => {
    expect(checkPasswordPolicy('short', [])).toBe('too_short');
    expect(checkPasswordPolicy('x'.repeat(129), [])).toBe('too_long');
    expect(checkPasswordPolicy('MyNameIsAlexandra99', ['alexandra@example.com'])).toBe('contains_identity');
    expect(checkPasswordPolicy('a perfectly fine passphrase', ['alex@example.com', 'Alex'])).toBeNull();
  });

  it('breach check uses k-anonymity and fails open', async () => {
    // SHA-1("password") = 5BAA61E4C9B93F3F0682250B6CF8331B7EE68FD8
    let requested = '';
    const fakeFetch = (async (url: string) => {
      requested = url;
      return new Response('1E4C9B93F3F0682250B6CF8331B7EE68FD8:3861493\nABCDEF:1\n');
    }) as unknown as typeof fetch;
    const checker = hibpBreachChecker(fakeFetch);
    expect(await checker.isBreached('password')).toBe(true);
    expect(requested).toBe('https://api.pwnedpasswords.com/range/5BAA6');
    expect(await checker.isBreached('a-unique-passphrase-123')).toBe(false);
    const failing = hibpBreachChecker((async () => {
      throw new Error('network down');
    }) as unknown as typeof fetch);
    expect(await failing.isBreached('password')).toBe(false);
  });
});

describe('Sign in with Apple token verification', () => {
  it('accepts a valid token and reads email claims', async () => {
    const apple = await FakeApple.create();
    const token = await apple.token({
      sub: 'apple-1',
      rawNonce: 'n0nce',
      email: 'x@privaterelay.appleid.com',
    });
    const identity = await apple.verifier().verify(token, 'n0nce');
    expect(identity).toMatchObject({
      subject: 'apple-1',
      email: 'x@privaterelay.appleid.com',
      emailVerified: true,
    });
  });

  it('rejects wrong nonce, audience, expiry and signer', async () => {
    const apple = await FakeApple.create();
    const other = await FakeApple.create();
    const verifier = apple.verifier();
    const ok = { sub: 'apple-1', rawNonce: 'n0nce' };
    await expect(verifier.verify(await apple.token(ok), 'different')).rejects.toThrow(AppleTokenError);
    await expect(
      verifier.verify(await apple.token({ ...ok, audience: 'com.other.app' }), 'n0nce'),
    ).rejects.toThrow(AppleTokenError);
    await expect(
      verifier.verify(await apple.token({ ...ok, expiresInSeconds: -60 }), 'n0nce'),
    ).rejects.toThrow(AppleTokenError);
    await expect(verifier.verify(await other.token(ok), 'n0nce')).rejects.toThrow(AppleTokenError);
  });
});
