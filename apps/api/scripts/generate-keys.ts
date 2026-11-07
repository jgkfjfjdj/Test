// Prints fresh production secrets for .env / the secret store. Run once per environment.
import { randomBytes } from 'node:crypto';
import { generatePrivateJwk } from '../src/lib/keys.js';

const access = await generatePrivateJwk(`access-${new Date().toISOString().slice(0, 10)}`);
const tickets = await generatePrivateJwk(`tickets-${new Date().toISOString().slice(0, 10)}`);
console.log(`ACCESS_TOKEN_KEYS='${JSON.stringify([access])}'`);
console.log(`TICKET_KEYS='${JSON.stringify([tickets])}'`);
console.log(`IP_HASH_SECRET=${randomBytes(32).toString('base64')}`);
console.log(`STAFF_SECRET_KEY=${randomBytes(32).toString('base64')}`);
console.log(
  '# Rotation: prepend a new key to the JSON array, deploy, and remove the old key after tokens expire.',
);
