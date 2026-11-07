import { fileURLToPath } from 'node:url';
import { drizzle } from 'drizzle-orm/postgres-js';
import { migrate } from 'drizzle-orm/postgres-js/migrator';
import postgres from 'postgres';

/** Default location of the SQL migrations shipped with this package. */
export const defaultMigrationsFolder = fileURLToPath(new URL('../migrations', import.meta.url));

/**
 * Applies all pending migrations. Uses a dedicated single connection and a
 * Postgres advisory lock (taken by drizzle's migrator inside a transaction) so
 * concurrent deploys cannot apply the same migration twice.
 */
export async function runMigrations(url: string, migrationsFolder = defaultMigrationsFolder): Promise<void> {
  const sql = postgres(url, { max: 1, onnotice: () => {} });
  try {
    await migrate(drizzle(sql), { migrationsFolder });
  } finally {
    await sql.end({ timeout: 5 });
  }
}
