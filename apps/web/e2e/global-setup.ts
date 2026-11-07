import { execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';

/** Resets the e2e database and creates an invited super admin; the tests read the setup token. */
export default function globalSetup(): void {
  const output = execFileSync(
    'pnpm',
    ['--silent', '--filter', '@kindling/api', 'exec', 'tsx', 'scripts/e2e-prepare.ts'],
    {
      env: {
        ...process.env,
        NODE_ENV: 'development',
        DATABASE_URL:
          process.env.TEST_DATABASE_URL ?? 'postgres://kindling:kindling@localhost:5432/kindling_test',
        REDIS_URL: process.env.TEST_REDIS_URL ?? 'redis://localhost:6379/15',
      },
      encoding: 'utf8',
    },
  );
  const line = output
    .trim()
    .split('\n')
    .reverse()
    .find((l) => l.startsWith('{'));
  if (!line) throw new Error(`e2e-prepare printed no token:\n${output}`);
  mkdirSync('e2e/.auth', { recursive: true });
  writeFileSync('e2e/.auth/state.json', line);
}
