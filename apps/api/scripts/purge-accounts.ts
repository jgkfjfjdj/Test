// Permanently deletes accounts whose deletion grace period has ended. Schedule daily (cron/systemd timer
// until the worker process exists). Usage: pnpm --filter @kindling/api accounts:purge
import { createDb } from '@kindling/db';
import { loadConfig } from '../src/config.js';
import { createContext } from '../src/context.js';
import { KeyRing } from '../src/lib/keys.js';
import { MemoryMailer } from '../src/lib/mailer.js';
import { noBreachCheck } from '../src/lib/passwords.js';
import { createRedis } from '../src/lib/redis.js';
import { AccountService } from '../src/modules/accounts/service.js';
import { AuditService } from '../src/modules/audit/service.js';

const config = loadConfig();
const db = createDb({ url: config.DATABASE_URL, maxConnections: 1 });
const redis = createRedis(config.REDIS_URL);
const unused = (await KeyRing.fromEnv('unused', undefined)).ring;
const ctx = createContext(config, {
  db,
  redis,
  mailer: new MemoryMailer(),
  keys: { access: unused, tickets: unused },
  apple: { verify: async () => Promise.reject(new Error('unused')) },
  breachChecker: noBreachCheck,
  probes: { postgres: { ping: async () => {} }, redis: { ping: async () => {} } },
});

try {
  const purged = await new AccountService(ctx).purgeExpiredDeletions();
  await new AuditService(ctx).record({ actorType: 'system', action: 'accounts.purge', after: { purged } });
  console.log(`Purged ${purged} account(s).`);
} catch (err) {
  console.error(err);
  process.exitCode = 1;
} finally {
  redis.disconnect();
  await db.close(1);
}
