import { customType, timestamp } from 'drizzle-orm/pg-core';

/** Case-insensitive text (requires the citext extension, migration 0000). */
export const citext = customType<{ data: string }>({
  dataType() {
    return 'citext';
  },
});

/** Binary data (hashes, encrypted secrets). postgres.js maps it to Buffer. */
export const bytea = customType<{ data: Buffer; driverData: Buffer }>({
  dataType() {
    return 'bytea';
  },
});

export const timestamptz = (name: string) => timestamp(name, { withTimezone: true, mode: 'date' });

export const createdAt = () => timestamptz('created_at').notNull().defaultNow();
export const updatedAt = () => timestamptz('updated_at').notNull().defaultNow();
