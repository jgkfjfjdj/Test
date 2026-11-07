import postgres from 'postgres';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { runMigrations } from '../src/migrate.js';

const url = process.env.TEST_DATABASE_URL;
if (!url) {
  throw new Error('TEST_DATABASE_URL must be set for integration tests (see .env.example).');
}

describe('migrations', () => {
  const sql = postgres(url, { max: 1, onnotice: () => {} });

  beforeAll(async () => {
    // Start from a clean slate so the test proves migrations work on an empty database.
    await sql`DROP SCHEMA IF EXISTS drizzle CASCADE`;
    await sql`DROP EXTENSION IF EXISTS pg_trgm`;
    await sql`DROP EXTENSION IF EXISTS citext`;
  });

  afterAll(async () => {
    await sql.end({ timeout: 5 });
  });

  it('applies all migrations to an empty database', async () => {
    await runMigrations(url);
    const rows = await sql<{ extname: string }[]>`
      SELECT extname FROM pg_extension WHERE extname IN ('citext', 'pg_trgm') ORDER BY extname`;
    expect(rows.map((r) => r.extname)).toEqual(['citext', 'pg_trgm']);
  });

  it('is idempotent when run again', async () => {
    await runMigrations(url);
    const rows = await sql<{ count: number }[]>`
      SELECT count(*)::int AS count FROM drizzle.__drizzle_migrations`;
    expect(rows[0]?.count).toBe(1);
  });
});
