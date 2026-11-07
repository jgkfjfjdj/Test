import { sql } from 'drizzle-orm';
import { check, index, jsonb, pgTable, primaryKey, text, uuid } from 'drizzle-orm/pg-core';
import { bytea, timestamptz } from './types.js';

export const AUDIT_ACTOR_TYPES = ['staff', 'owner', 'server', 'system'] as const;
export type AuditActorType = (typeof AUDIT_ACTOR_TYPES)[number];

/**
 * Append-only audit trail. The table is RANGE-partitioned by occurred_at in the SQL migration
 * (a DEFAULT partition catches everything until monthly partitions are added by a scheduled job),
 * so the primary key includes the partition column.
 */
export const auditLog = pgTable(
  'audit_log',
  {
    id: uuid('id').notNull(),
    occurredAt: timestamptz('occurred_at').notNull().defaultNow(),
    actorType: text('actor_type').$type<AuditActorType>().notNull(),
    actorId: uuid('actor_id'),
    action: text('action').notNull(),
    targetType: text('target_type'),
    targetId: text('target_id'),
    reason: text('reason'),
    before: jsonb('before').$type<unknown>(),
    after: jsonb('after').$type<unknown>(),
    result: text('result').notNull().default('ok'),
    requestId: text('request_id'),
    ipHash: bytea('ip_hash'),
  },
  (t) => [
    primaryKey({ name: 'pk_audit_log', columns: [t.id, t.occurredAt] }),
    index('ix_audit_log_target').on(t.targetType, t.targetId, t.occurredAt),
    index('ix_audit_log_actor').on(t.actorType, t.actorId, t.occurredAt),
    index('ix_audit_log_action').on(t.action, t.occurredAt),
    check('ck_audit_log_actor_type', sql`${t.actorType} IN ('staff','owner','server','system')`),
  ],
);
