import { auditLog, newId, type AuditActorType, type Database } from '@kindling/db';
import { and, desc, eq, lt, or, type SQL } from 'drizzle-orm';
import type { FastifyRequest } from 'fastify';
import type { AppContext } from '../../context.js';

export interface AuditEntry {
  actorType: AuditActorType;
  actorId?: string | null;
  action: string;
  targetType?: string | null;
  targetId?: string | null;
  reason?: string | null;
  before?: unknown;
  after?: unknown;
  result?: string;
  requestId?: string | null;
  ipHash?: Buffer | null;
}

type Executor = Pick<Database, 'insert'>;

/**
 * Append-only audit trail. Write the entry with the same transaction (`executor`) as the change it
 * describes, so a change can never exist without its audit record. Do not put player PII
 * (emails, IPs) into before/after — only ids and the changed fields.
 */
export class AuditService {
  constructor(private readonly ctx: AppContext) {}

  async record(entry: AuditEntry, executor: Executor = this.ctx.db): Promise<string> {
    const id = newId();
    await executor.insert(auditLog).values({
      id,
      occurredAt: this.ctx.now(),
      actorType: entry.actorType,
      actorId: entry.actorId ?? null,
      action: entry.action,
      targetType: entry.targetType ?? null,
      targetId: entry.targetId ?? null,
      reason: entry.reason ?? null,
      before: entry.before ?? null,
      after: entry.after ?? null,
      result: entry.result ?? 'ok',
      requestId: entry.requestId ?? null,
      ipHash: entry.ipHash ?? null,
    });
    return id;
  }

  /** Convenience for staff actions taken in an HTTP request. */
  async recordStaff(
    request: FastifyRequest,
    entry: Omit<AuditEntry, 'actorType' | 'actorId' | 'requestId' | 'ipHash'>,
    executor?: Executor,
  ) {
    return this.record(
      {
        ...entry,
        actorType: 'staff',
        actorId: request.staff?.id ?? null,
        requestId: request.id,
        ipHash: this.ctx.hashIp(request.ip),
      },
      executor,
    );
  }

  async list(filter: {
    actorId?: string | undefined;
    action?: string | undefined;
    targetType?: string | undefined;
    targetId?: string | undefined;
    cursor?: string | undefined;
    limit: number;
  }) {
    const conditions: SQL[] = [];
    if (filter.actorId) conditions.push(eq(auditLog.actorId, filter.actorId));
    if (filter.action) conditions.push(eq(auditLog.action, filter.action));
    if (filter.targetType) conditions.push(eq(auditLog.targetType, filter.targetType));
    if (filter.targetId) conditions.push(eq(auditLog.targetId, filter.targetId));
    if (filter.cursor) {
      const [at, id] = JSON.parse(Buffer.from(filter.cursor, 'base64url').toString()) as [string, string];
      const atDate = new Date(at);
      conditions.push(
        or(lt(auditLog.occurredAt, atDate), and(eq(auditLog.occurredAt, atDate), lt(auditLog.id, id)))!,
      );
    }
    const rows = await this.ctx.db
      .select()
      .from(auditLog)
      .where(conditions.length ? and(...conditions) : undefined)
      .orderBy(desc(auditLog.occurredAt), desc(auditLog.id))
      .limit(filter.limit + 1);
    const page = rows.slice(0, filter.limit);
    const last = page[page.length - 1];
    return {
      items: page.map((r) => ({
        id: r.id,
        occurredAt: r.occurredAt.toISOString(),
        actorType: r.actorType,
        actorId: r.actorId,
        action: r.action,
        targetType: r.targetType,
        targetId: r.targetId,
        reason: r.reason,
        before: r.before ?? null,
        after: r.after ?? null,
        result: r.result,
        requestId: r.requestId,
      })),
      nextCursor:
        rows.length > filter.limit && last
          ? Buffer.from(JSON.stringify([last.occurredAt.toISOString(), last.id])).toString('base64url')
          : null,
    };
  }
}
