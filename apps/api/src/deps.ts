import { createDb } from '@kindling/db';
import type { FastifyBaseLogger } from 'fastify';
import type { AppConfig } from './config.js';
import type { AppDeps } from './context.js';
import { JwksAppleTokenVerifier } from './lib/apple.js';
import { KeyRing } from './lib/keys.js';
import { MemoryMailer, SmtpMailer, type Mailer } from './lib/mailer.js';
import { hibpBreachChecker, noBreachCheck } from './lib/passwords.js';
import { createRedis } from './lib/redis.js';

export interface RuntimeDeps extends AppDeps {
  close: () => Promise<void>;
  /** Warnings to log once the logger exists (e.g. ephemeral keys). */
  warnings: string[];
}

/** Creates the real connections and services used by the running process. */
export async function createRuntimeDeps(config: AppConfig, log?: FastifyBaseLogger): Promise<RuntimeDeps> {
  const warnings: string[] = [];
  const database = createDb({
    url: config.DATABASE_URL,
    maxConnections: config.DB_POOL_MAX,
    applicationName: 'kindling-api',
  });
  const redis = createRedis(config.REDIS_URL);

  const access = await KeyRing.fromEnv('kindling-api', config.ACCESS_TOKEN_KEYS);
  const tickets = await KeyRing.fromEnv('kindling-tickets', config.TICKET_KEYS);
  if (access.ephemeral)
    warnings.push('ACCESS_TOKEN_KEYS not set: using an ephemeral key (tokens die on restart)');
  if (tickets.ephemeral)
    warnings.push('TICKET_KEYS not set: using an ephemeral key (tickets die on restart)');
  if (!config.IP_HASH_SECRET) warnings.push('IP_HASH_SECRET not set: using the public development secret');
  if (!config.STAFF_SECRET_KEY) warnings.push('STAFF_SECRET_KEY not set: using the public development key');

  let mailer: Mailer;
  let closeMailer = () => {};
  if (config.MAIL_TRANSPORT === 'smtp' && config.SMTP_URL) {
    const smtp = new SmtpMailer(config.SMTP_URL, config.MAIL_FROM);
    mailer = smtp;
    closeMailer = () => smtp.close();
  } else {
    mailer = new MemoryMailer(log);
  }

  return {
    db: database,
    redis,
    mailer,
    keys: { access: access.ring, tickets: tickets.ring },
    apple: JwksAppleTokenVerifier.forApple(
      config.APPLE_CLIENT_IDS.split(',')
        .map((s) => s.trim())
        .filter(Boolean),
    ),
    breachChecker: config.PASSWORD_BREACH_CHECK ? hibpBreachChecker() : noBreachCheck,
    probes: {
      postgres: {
        ping: async () => {
          await database.sql`SELECT 1`;
        },
      },
      redis: {
        ping: async () => {
          await redis.ping();
        },
      },
    },
    warnings,
    close: async () => {
      closeMailer();
      await Promise.allSettled([database.close(), redis.quit()]);
    },
  };
}
