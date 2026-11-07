// Writes the OpenAPI document to docs/api/openapi.json (CI fails if it is out of date).
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildTestApp } from '../test/support/unit.js';

const here = dirname(fileURLToPath(import.meta.url));
const outFile = resolve(here, '../../../docs/api/openapi.json');

const app = await buildTestApp();
await app.ready();
const doc = app.swagger();
mkdirSync(dirname(outFile), { recursive: true });
writeFileSync(outFile, `${JSON.stringify(doc, null, 2)}\n`);
await app.close();
console.log(`OpenAPI written to ${outFile}`);
