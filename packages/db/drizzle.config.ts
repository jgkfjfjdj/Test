import { defineConfig } from 'drizzle-kit';

export default defineConfig({
  dialect: 'postgresql',
  schema: './src/schema/index.ts',
  out: './migrations',
  dbCredentials: { url: process.env.DATABASE_URL ?? 'postgres://kindling:kindling@localhost:5432/kindling' },
  strict: true,
  verbose: true,
});
