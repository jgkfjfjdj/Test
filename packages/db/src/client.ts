import { drizzle, type PostgresJsDatabase } from 'drizzle-orm/postgres-js';
import postgres from 'postgres';
import * as schema from './schema/index.js';

export type Database = PostgresJsDatabase<typeof schema>;

export interface DbHandle {
  /** Drizzle query builder bound to the pool. */
  db: Database;
  /** Raw postgres.js client (for health checks and admin/monitoring queries). */
  sql: postgres.Sql;
  /** Closes all pooled connections (waits up to `timeoutSeconds` for active queries). */
  close: (timeoutSeconds?: number) => Promise<void>;
}

export interface CreateDbOptions {
  url: string;
  /** Max pooled connections for this process. */
  maxConnections?: number;
  /** Application name visible in pg_stat_activity. */
  applicationName?: string;
}

export function createDb(options: CreateDbOptions): DbHandle {
  const sql = postgres(options.url, {
    max: options.maxConnections ?? 10,
    idle_timeout: 30,
    connect_timeout: 10,
    onnotice: () => {},
    connection: { application_name: options.applicationName ?? 'kindling' },
  });
  const db = drizzle(sql, { schema });
  return {
    db,
    sql,
    close: (timeoutSeconds = 5) => sql.end({ timeout: timeoutSeconds }),
  };
}
