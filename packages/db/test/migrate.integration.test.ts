import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import postgres from 'postgres';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { DEFAULT_ROLES } from '@kindling/contracts';
import { runMigrations } from '../src/migrate.js';

const url = process.env.TEST_DATABASE_URL;
if (!url) {
  throw new Error('TEST_DATABASE_URL must be set for integration tests (see .env.example).');
}

const journal = JSON.parse(
  readFileSync(fileURLToPath(new URL('../migrations/meta/_journal.json', import.meta.url)), 'utf8'),
) as { entries: unknown[] };

describe('migrations', () => {
  const sql = postgres(url, { max: 1, onnotice: () => {} });

  beforeAll(async () => {
    // Start from an empty database so the test proves the full migration chain works.
    await sql`DROP SCHEMA IF EXISTS drizzle CASCADE`;
    await sql`DROP SCHEMA public CASCADE`;
    await sql`CREATE SCHEMA public`;
  });

  afterAll(async () => {
    await sql.end({ timeout: 5 });
  });

  it('applies all migrations to an empty database', async () => {
    await runMigrations(url);
    const ext = await sql<{ extname: string }[]>`
      SELECT extname FROM pg_extension WHERE extname IN ('citext', 'pg_trgm') ORDER BY extname`;
    expect(ext.map((r) => r.extname)).toEqual(['citext', 'pg_trgm']);
    const tables = await sql<{ tablename: string }[]>`
      SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename`;
    expect(tables.map((t) => t.tablename)).toEqual(
      expect.arrayContaining(['accounts', 'refresh_tokens', 'game_servers', 'staff_users', 'audit_log']),
    );
  });

  it('is idempotent when run again', async () => {
    await runMigrations(url);
    const rows = await sql<{ count: number }[]>`
      SELECT count(*)::int AS count FROM drizzle.__drizzle_migrations`;
    expect(rows[0]?.count).toBe(journal.entries.length);
  });

  it('seeds staff roles that match the permission catalogue', async () => {
    const rows = await sql<{ id: string; permissions: string[] }[]>`SELECT id, permissions FROM staff_roles`;
    const fromDb = Object.fromEntries(rows.map((r) => [r.id, [...r.permissions].sort()]));
    const expected = Object.fromEntries(
      Object.entries(DEFAULT_ROLES).map(([id, role]) => [id, [...role.permissions].sort()]),
    );
    expect(fromDb).toEqual(expected);
  });

  it('keeps audit_log append-only', async () => {
    const id = '0190a000-0000-7000-8000-000000000001';
    await sql`INSERT INTO audit_log (id, actor_type, action) VALUES (${id}, 'system', 'test.insert')`;
    await expect(sql`UPDATE audit_log SET action = 'tampered' WHERE id = ${id}`).rejects.toThrow(
      /append-only/,
    );
    await expect(sql`DELETE FROM audit_log WHERE id = ${id}`).rejects.toThrow(/append-only/);
    const rows = await sql<{ action: string }[]>`SELECT action FROM audit_log WHERE id = ${id}`;
    expect(rows[0]?.action).toBe('test.insert');
  });
});
