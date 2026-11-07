// Bootstraps staff access: creates an invited staff user and prints the one-time setup link.
// Usage: pnpm --filter @kindling/api staff:invite -- --email you@studio.com --name "You" --role super_admin
import { parseArgs } from 'node:util';
import { createDb } from '@kindling/db';
import { loadConfig } from '../src/config.js';
import { createContext } from '../src/context.js';
import { KeyRing } from '../src/lib/keys.js';
import { MemoryMailer } from '../src/lib/mailer.js';
import { noBreachCheck } from '../src/lib/passwords.js';
import { createRedis } from '../src/lib/redis.js';
import { AuditService } from '../src/modules/audit/service.js';
import { StaffService } from '../src/modules/staff/service.js';

const { values } = parseArgs({
  options: {
    email: { type: 'string' },
    name: { type: 'string' },
    role: { type: 'string', multiple: true },
  },
});
if (!values.email || !values.name || !values.role?.length) {
  console.error('Usage: staff:invite -- --email <email> --name <name> --role <role> [--role <role>...]');
  process.exit(2);
}

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
  const staff = new StaffService(ctx);
  const { id, setupToken } = await staff.invite({
    email: values.email,
    name: values.name,
    roles: values.role,
  });
  await new AuditService(ctx).record({
    actorType: 'system',
    action: 'staff.invite',
    targetType: 'staff',
    targetId: id,
    reason: 'bootstrap via staff:invite CLI',
    after: { email: values.email, name: values.name, roles: values.role },
  });
  console.log(`Invited ${values.email} (${values.role.join(', ')}).`);
  console.log(`Setup link (valid 72 h): ${staff.setupUrl(setupToken)}`);
  console.log(`Setup token: ${setupToken}`);
} catch (err) {
  console.error(err instanceof Error ? err.message : err);
  process.exitCode = 1;
} finally {
  redis.disconnect();
  await db.close(1);
}
