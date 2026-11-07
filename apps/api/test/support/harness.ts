import { createHash } from 'node:crypto';
import { createDb, type DbHandle } from '@kindling/db';
import type { FastifyInstance, InjectOptions, LightMyRequestResponse } from 'fastify';
import { createLocalJWKSet, exportJWK, generateKeyPair, SignJWT, type JWK } from 'jose';
import { buildApp } from '../../src/app.js';
import type { AppConfig } from '../../src/config.js';
import { APPLE_ISSUER, JwksAppleTokenVerifier } from '../../src/lib/apple.js';
import { KeyRing } from '../../src/lib/keys.js';
import { MemoryMailer } from '../../src/lib/mailer.js';
import { noBreachCheck, type BreachChecker } from '../../src/lib/passwords.js';
import { createRedis } from '../../src/lib/redis.js';
import { testConfig } from './unit.js';

export const TEST_APPLE_CLIENT_ID = 'com.example.kindling';

/** Mints Sign in with Apple identity tokens with a local RSA key (stands in for Apple's JWKS). */
export class FakeApple {
  private constructor(
    private readonly privateKey: Awaited<ReturnType<typeof generateKeyPair>>['privateKey'],
    readonly publicJwk: JWK,
  ) {}

  static async create(): Promise<FakeApple> {
    const { privateKey, publicKey } = await generateKeyPair('RS256', { extractable: true });
    return new FakeApple(privateKey, { ...(await exportJWK(publicKey)), kid: 'test-apple', alg: 'RS256' });
  }

  verifier(): JwksAppleTokenVerifier {
    return new JwksAppleTokenVerifier([TEST_APPLE_CLIENT_ID], createLocalJWKSet({ keys: [this.publicJwk] }));
  }

  async token(options: {
    sub: string;
    rawNonce: string;
    email?: string;
    emailVerified?: boolean;
    audience?: string;
    expiresInSeconds?: number;
  }): Promise<string> {
    const claims: Record<string, unknown> = {
      nonce: createHash('sha256').update(options.rawNonce).digest('hex'),
      nonce_supported: true,
    };
    if (options.email) {
      claims['email'] = options.email;
      claims['email_verified'] = options.emailVerified === false ? 'false' : 'true';
    }
    const now = Math.floor(Date.now() / 1000);
    return new SignJWT(claims)
      .setProtectedHeader({ alg: 'RS256', kid: 'test-apple' })
      .setIssuer(APPLE_ISSUER)
      .setAudience(options.audience ?? TEST_APPLE_CLIENT_ID)
      .setSubject(options.sub)
      .setIssuedAt(now)
      .setExpirationTime(now + (options.expiresInSeconds ?? 600))
      .sign(this.privateKey);
  }
}

/** Mutable test clock. */
export class TestClock {
  private offsetMs = 0;
  now = (): Date => new Date(Date.now() + this.offsetMs);
  advance(ms: number): void {
    this.offsetMs += ms;
  }
}

export interface Harness {
  app: FastifyInstance;
  config: AppConfig;
  db: DbHandle;
  mailer: MemoryMailer;
  apple: FakeApple;
  clock: TestClock;
  keys: { access: KeyRing; tickets: KeyRing };
  inject(options: InjectOptions): Promise<LightMyRequestResponse>;
  /** Another API instance on the same database, Redis and keys (multi-instance tests). */
  peer(): Promise<FastifyInstance>;
  close(): Promise<void>;
}

const MUTABLE_TABLES = [
  'accounts',
  'email_tokens',
  'refresh_tokens',
  'auth_identities',
  'profiles',
  'cloud_saves',
  'game_servers',
  'server_tokens',
  'play_sessions',
  'staff_users',
  'staff_user_roles',
  'staff_sessions',
  'maintenance_windows',
  'remote_config',
  'audit_log',
];

/**
 * Real PostgreSQL + Redis app for integration tests. Each call wipes mutable tables and the Redis
 * test database, so test files start clean (files run one at a time — see vitest.integration.config.ts).
 */
export async function createHarness(
  overrides: Record<string, string> = {},
  options: { breachChecker?: BreachChecker } = {},
): Promise<Harness> {
  const config = testConfig({
    DATABASE_URL: process.env.TEST_DATABASE_URL!,
    REDIS_URL: process.env.TEST_REDIS_URL!,
    PUBLIC_BASE_URL: 'https://api.test.invalid',
    APPLE_CLIENT_IDS: TEST_APPLE_CLIENT_ID,
    ...overrides,
  });
  const db = createDb({ url: config.DATABASE_URL, maxConnections: 5 });
  const redis = createRedis(config.REDIS_URL);
  await redis.connect();
  await redis.flushdb();
  await db.sql.unsafe(`TRUNCATE ${MUTABLE_TABLES.map((t) => `"${t}"`).join(', ')} RESTART IDENTITY CASCADE`);
  await db.sql`UPDATE version_policy SET min_supported_build = 0, latest_build = 0`;

  const mailer = new MemoryMailer();
  const apple = await FakeApple.create();
  const clock = new TestClock();
  const keys = {
    access: (await KeyRing.fromEnv('kindling-api', undefined)).ring,
    tickets: (await KeyRing.fromEnv('kindling-tickets', undefined)).ring,
  };

  const instance = async (instanceDb: DbHandle, instanceRedis: typeof redis) =>
    buildApp({
      config,
      logger: false,
      deps: {
        db: instanceDb,
        redis: instanceRedis,
        mailer,
        keys,
        apple: apple.verifier(),
        breachChecker: options.breachChecker ?? noBreachCheck,
        now: clock.now,
        probes: {
          postgres: { ping: async () => void (await instanceDb.sql`SELECT 1`) },
          redis: { ping: async () => void (await instanceRedis.ping()) },
        },
        close: async () => {
          await Promise.allSettled([instanceDb.close(1), instanceRedis.quit()]);
        },
      },
    });

  const app = await instance(db, redis);
  await app.ready();
  const peers: FastifyInstance[] = [];

  return {
    app,
    config,
    db,
    mailer,
    apple,
    clock,
    keys,
    inject: (opts) => app.inject(opts),
    peer: async () => {
      const peerRedis = createRedis(config.REDIS_URL);
      await peerRedis.connect();
      const peerApp = await instance(createDb({ url: config.DATABASE_URL, maxConnections: 2 }), peerRedis);
      await peerApp.ready();
      peers.push(peerApp);
      return peerApp;
    },
    close: async () => {
      await Promise.all(peers.map((p) => p.close()));
      await app.close();
    },
  };
}

/** Pulls the token query parameter out of the last email sent to an address. */
export function tokenFromMail(mailer: MemoryMailer, to: string, path: string): string {
  const mail = mailer.lastTo(to);
  if (!mail) throw new Error(`no mail sent to ${to}`);
  const match = new RegExp(`${path.replace(/[/.]/g, '\\$&')}\\?token=([A-Za-z0-9_-]+)`).exec(mail.text);
  if (!match?.[1]) throw new Error(`no ${path} link in mail to ${to}:\n${mail.text}`);
  return match[1];
}
