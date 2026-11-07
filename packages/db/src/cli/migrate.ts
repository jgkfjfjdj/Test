// CLI: `pnpm db:migrate` (uses DATABASE_URL) — also bundled into the API image as dist/migrate.js.
import { runMigrations, defaultMigrationsFolder } from '../migrate.js';

const url = process.env.DATABASE_URL;
if (!url) {
  console.error('DATABASE_URL is not set.');
  process.exit(2);
}
const folder = process.env.MIGRATIONS_DIR ?? defaultMigrationsFolder;

try {
  await runMigrations(url, folder);
  console.log(`Migrations applied from ${folder}`);
} catch (err) {
  console.error('Migration failed:', err);
  process.exit(1);
}
