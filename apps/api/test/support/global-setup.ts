// Vitest globalSetup for integration tests: one fresh, fully migrated database per run.
import postgres from 'postgres';
import { runMigrations } from '@kindling/db';

export default async function setup(): Promise<void> {
  const url = process.env.TEST_DATABASE_URL;
  if (!url || !process.env.TEST_REDIS_URL) {
    throw new Error(
      'TEST_DATABASE_URL and TEST_REDIS_URL must be set for integration tests (see .env.example).',
    );
  }
  const sql = postgres(url, { max: 1, onnotice: () => {} });
  try {
    await sql`DROP SCHEMA IF EXISTS drizzle CASCADE`;
    await sql`DROP SCHEMA public CASCADE`;
    await sql`CREATE SCHEMA public`;
  } finally {
    await sql.end({ timeout: 5 });
  }
  await runMigrations(url);
}
