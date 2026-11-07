// Prepares a throwaway database for browser end-to-end tests: drops and re-migrates the schema,
// flushes the Redis database, and invites a super admin. Prints {"setupToken": "..."}.
// Refuses to touch any database whose name doesn't contain "test" or "e2e".
import postgres from 'postgres';
import { createDb, runMigrations } from '@kindling/db';
import { loadConfig } from '../src/config.js';
import { createContext } from '../src/context.js';
import { KeyRing } from '../src/lib/keys.js';
import { MemoryMailer } from '../src/lib/mailer.js';
import { noBreachCheck } from '../src/lib/passwords.js';
import { createRedis } from '../src/lib/redis.js';
import { StaffService } from '../src/modules/staff/service.js';

const config = loadConfig();
const dbName = new URL(config.DATABASE_URL).pathname.slice(1);
if (!/test|e2e/.test(dbName)) {
  console.error(`Refusing to reset database "${dbName}": its name must contain "test" or "e2e".`);
  process.exit(2);
}

const sql = postgres(config.DATABASE_URL, { max: 1, onnotice: () => {} });
await sql`DROP SCHEMA IF EXISTS drizzle CASCADE`;
await sql`DROP SCHEMA public CASCADE`;
await sql`CREATE SCHEMA public`;
await sql.end({ timeout: 5 });
await runMigrations(config.DATABASE_URL);

const redis = createRedis(config.REDIS_URL);
await redis.connect();
await redis.flushdb();

const db = createDb({ url: config.DATABASE_URL, maxConnections: 1 });
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
const { setupToken } = await new StaffService(ctx).invite({
  email: 'e2e-lead@studio.example',
  name: 'Avery Lead',
  roles: ['super_admin'],
});
console.log(JSON.stringify({ setupToken }));
redis.disconnect();
await db.close(1);
