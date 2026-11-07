import { hash, verify } from '@node-rs/argon2';
import { createHash } from 'node:crypto';

/**
 * Argon2id parameters: 19 MiB memory, 2 iterations, 1 lane (OWASP minimum recommendation).
 * Re-tune to ~50 ms on production hardware; old hashes keep verifying because parameters are encoded in them.
 */
// algorithm 2 = Argon2id (the package's Algorithm enum is a const enum, unusable with verbatimModuleSyntax).
const OPTIONS = { algorithm: 2, memoryCost: 19_456, timeCost: 2, parallelism: 1 } as const;

export const PASSWORD_MIN_LENGTH = 10;
export const PASSWORD_MAX_LENGTH = 128;

export function hashPassword(password: string): Promise<string> {
  return hash(password, OPTIONS);
}

export async function verifyPassword(passwordHash: string, password: string): Promise<boolean> {
  try {
    return await verify(passwordHash, password);
  } catch {
    return false;
  }
}

let dummyHash: Promise<string> | undefined;
/**
 * Burns the same CPU time as a real verification. Used when the account doesn't exist,
 * so response timing doesn't reveal which emails are registered.
 */
export async function verifyAgainstDummy(password: string): Promise<false> {
  dummyHash ??= hashPassword('kindling-timing-equalizer-password');
  await verifyPassword(await dummyHash, password);
  return false;
}

export type PasswordProblem = 'too_short' | 'too_long' | 'contains_identity' | 'breached';

/** Local password policy: length bounds and not containing the user's own email or name. */
export function checkPasswordPolicy(
  password: string,
  identity: (string | null | undefined)[],
): PasswordProblem | null {
  if ([...password].length < PASSWORD_MIN_LENGTH) return 'too_short';
  if ([...password].length > PASSWORD_MAX_LENGTH) return 'too_long';
  const lower = password.toLowerCase();
  for (const value of identity) {
    if (!value) continue;
    const local = value.toLowerCase().split('@')[0] ?? '';
    if (local.length >= 4 && lower.includes(local)) return 'contains_identity';
  }
  return null;
}

export interface BreachChecker {
  /** Resolves true if the password appears in a breach corpus. Must fail open (false) on errors. */
  isBreached(password: string): Promise<boolean>;
}

export const noBreachCheck: BreachChecker = { isBreached: async () => false };

/**
 * Have I Been Pwned range API (k-anonymity: only the first 5 hex chars of the SHA-1 leave the server).
 * Fails open after a short timeout so an outage never blocks registration.
 */
export function hibpBreachChecker(fetchImpl: typeof fetch = fetch, timeoutMs = 1500): BreachChecker {
  return {
    async isBreached(password) {
      const digest = createHash('sha1').update(password).digest('hex').toUpperCase();
      const prefix = digest.slice(0, 5);
      const suffix = digest.slice(5);
      try {
        const res = await fetchImpl(`https://api.pwnedpasswords.com/range/${prefix}`, {
          headers: { 'Add-Padding': 'true', 'User-Agent': 'kindling-api' },
          signal: AbortSignal.timeout(timeoutMs),
        });
        if (!res.ok) return false;
        const body = await res.text();
        return body.split('\n').some((line) => {
          const [hashSuffix, count] = line.trim().split(':');
          return hashSuffix === suffix && Number(count) > 0;
        });
      } catch {
        return false;
      }
    },
  };
}
