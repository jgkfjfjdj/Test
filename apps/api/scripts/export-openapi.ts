// Writes the OpenAPI document to docs/api/openapi.json (CI fails if it is out of date).
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildApp } from '../src/app.js';
import { loadConfig } from '../src/config.js';

const here = dirname(fileURLToPath(import.meta.url));
const outFile = resolve(here, '../../../docs/api/openapi.json');

const noop = { ping: async () => {} };
const app = await buildApp({
  config: loadConfig({ DATABASE_URL: 'postgres://openapi', REDIS_URL: 'redis://openapi', NODE_ENV: 'test' }),
  deps: { probes: { postgres: noop, redis: noop } },
  logger: false,
});
await app.ready();
const doc = app.swagger();
mkdirSync(dirname(outFile), { recursive: true });
writeFileSync(outFile, `${JSON.stringify(doc, null, 2)}\n`);
await app.close();
console.log(`OpenAPI written to ${outFile}`);
