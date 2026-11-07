import type { Database, DbHandle } from '@kindling/db';
import type { Redis } from 'ioredis';
import type { AppConfig } from './config.js';
import { DEV_IP_HASH_SECRET, DEV_STAFF_SECRET_KEY } from './config.js';
import type { AppleTokenVerifier } from './lib/apple.js';
import { hmacSha256 } from './lib/crypto.js';
import type { KeyRing } from './lib/keys.js';
import type { Mailer } from './lib/mailer.js';
import type { BreachChecker } from './lib/passwords.js';
import type { Pingable } from './modules/health/routes.js';

/** External dependencies handed to the app (real ones in main.ts, test doubles where useful). */
export interface AppDeps {
  db: DbHandle;
  redis: Redis;
  mailer: Mailer;
  keys: { access: KeyRing; tickets: KeyRing };
  apple: AppleTokenVerifier;
  breachChecker: BreachChecker;
  /** Readiness probes. */
  probes: { postgres: Pingable; redis: Pingable };
  /** Clock (tests freeze or advance it). */
  now?: () => Date;
  /** Called once when the server closes (release pools/connections). */
  close?: () => Promise<void>;
}

/** Everything a route module or service needs, built once per app. */
export interface AppContext {
  config: AppConfig;
  db: Database;
  dbHandle: DbHandle;
  redis: Redis;
  mailer: Mailer;
  keys: { access: KeyRing; tickets: KeyRing };
  apple: AppleTokenVerifier;
  breachChecker: BreachChecker;
  now: () => Date;
  /** HMAC of an IP address (raw IPs are never stored). */
  hashIp: (ip: string) => Buffer;
  /** AES key for staff TOTP secrets. */
  staffSecretKey: Buffer;
}

export function createContext(config: AppConfig, deps: AppDeps): AppContext {
  const ipKey = Buffer.from(config.IP_HASH_SECRET ?? DEV_IP_HASH_SECRET, 'base64');
  return {
    config,
    db: deps.db.db,
    dbHandle: deps.db,
    redis: deps.redis,
    mailer: deps.mailer,
    keys: deps.keys,
    apple: deps.apple,
    breachChecker: deps.breachChecker,
    now: deps.now ?? (() => new Date()),
    hashIp: (ip) => hmacSha256(ipKey, ip),
    staffSecretKey: Buffer.from(config.STAFF_SECRET_KEY ?? DEV_STAFF_SECRET_KEY, 'base64'),
  };
}
