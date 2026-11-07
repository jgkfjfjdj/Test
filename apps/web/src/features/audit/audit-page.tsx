import { useInfiniteQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { ChevronDown, Search } from 'lucide-react';
import { useState } from 'react';
import { call, client, errorMessage } from '../../api/client';
import type { AuditItem } from '../../api/types';
import { RequirePermission } from '../../app/permissions';
import {
  Alert,
  Badge,
  Button,
  Code,
  EmptyState,
  Field,
  Input,
  JsonBlock,
  PageHeader,
  Spinner,
} from '../../components/ui';
import { formatDateTime } from '../../lib/format';

export interface AuditFilters {
  action?: string | undefined;
  targetType?: string | undefined;
  targetId?: string | undefined;
  actorId?: string | undefined;
}

export function AuditPage({ filters }: { filters: AuditFilters }) {
  return (
    <RequirePermission permission="audit.read">
      <Audit filters={filters} />
    </RequirePermission>
  );
}

/** Top-level keys whose values differ between before and after. */
export function changedKeys(before: unknown, after: unknown): string[] {
  if (!before || !after || typeof before !== 'object' || typeof after !== 'object') return [];
  const b = before as Record<string, unknown>;
  const a = after as Record<string, unknown>;
  return [...new Set([...Object.keys(b), ...Object.keys(a)])].filter(
    (k) => k in a && JSON.stringify(a[k]) !== JSON.stringify(b[k]),
  );
}

function Entry({ item }: { item: AuditItem }) {
  const changed = changedKeys(item.before, item.after);
  return (
    <li>
      <details className="group rounded-xl border border-line bg-surface">
        <summary className="flex min-h-11 cursor-pointer list-none items-start justify-between gap-3 px-4 py-3">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <Code>{item.action}</Code>
              {item.result !== 'ok' && <Badge tone="danger">{item.result}</Badge>}
              {item.targetType && (
                <span className="truncate text-xs text-muted">
                  {item.targetType} {item.targetId}
                </span>
              )}
            </div>
            <div className="mt-1 text-sm text-muted">
              {formatDateTime(item.occurredAt)} · {item.actorType}
              {item.actorId ? ` ${item.actorId.slice(0, 8)}…` : ''}
              {item.reason && <span className="text-ink"> — “{item.reason}”</span>}
            </div>
          </div>
          <ChevronDown
            className="mt-1 size-4 shrink-0 text-muted transition-transform group-open:rotate-180"
            aria-hidden
          />
        </summary>
        <div className="space-y-3 border-t border-line px-4 py-3 text-sm">
          {changed.length > 0 && (
            <p>
              Changed:{' '}
              {changed.map((k) => (
                <Code key={k} className="mr-1">
                  {k}
                </Code>
              ))}
            </p>
          )}
          <div className="grid gap-3 md:grid-cols-2">
            <div>
              <div className="mb-1 text-xs font-medium text-muted uppercase">Before</div>
              <JsonBlock value={item.before} />
            </div>
            <div>
              <div className="mb-1 text-xs font-medium text-muted uppercase">After</div>
              <JsonBlock value={item.after} />
            </div>
          </div>
          <p className="text-xs text-muted">
            Entry {item.id}
            {item.requestId && ` · request ${item.requestId}`}
            {item.actorId && ` · actor ${item.actorId}`}
          </p>
        </div>
      </details>
    </li>
  );
}

function Audit({ filters }: { filters: AuditFilters }) {
  const navigate = useNavigate();
  const [draft, setDraft] = useState<AuditFilters>(filters);
  const query = useInfiniteQuery({
    queryKey: ['audit', filters],
    initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam }) =>
      call(() =>
        client.GET('/admin/v1/audit', {
          params: {
            query: {
              limit: 30,
              ...(pageParam ? { cursor: pageParam } : {}),
              ...Object.fromEntries(Object.entries(filters).filter(([, v]) => v)),
            },
          },
        }),
      ),
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  });
  const items = query.data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <>
      <PageHeader
        title="Audit log"
        description="Every staff and system change, newest first. Entries can't be edited or deleted."
      />
      <form
        className="mb-5 grid gap-3 rounded-xl border border-line bg-surface p-4 sm:grid-cols-2 lg:grid-cols-[1fr_1fr_1fr_auto]"
        onSubmit={(e) => {
          e.preventDefault();
          const clean = Object.fromEntries(
            Object.entries(draft).map(([k, v]) => [k, v?.trim() || undefined]),
          );
          void navigate({ to: '/audit', search: clean });
        }}
      >
        <Field label="Action">
          {(p) => (
            <Input
              {...p}
              placeholder="e.g. config.update"
              value={draft.action ?? ''}
              onChange={(e) => setDraft({ ...draft, action: e.target.value })}
            />
          )}
        </Field>
        <Field label="Target type">
          {(p) => (
            <Input
              {...p}
              placeholder="server, staff, remote_config…"
              value={draft.targetType ?? ''}
              onChange={(e) => setDraft({ ...draft, targetType: e.target.value })}
            />
          )}
        </Field>
        <Field label="Target id">
          {(p) => (
            <Input
              {...p}
              value={draft.targetId ?? ''}
              onChange={(e) => setDraft({ ...draft, targetId: e.target.value })}
            />
          )}
        </Field>
        <div className="flex items-end gap-2">
          <Button type="submit" variant="primary" icon={<Search className="size-4" aria-hidden />}>
            Filter
          </Button>
          {Object.values(filters).some(Boolean) && (
            <Button
              onClick={() => {
                setDraft({});
                void navigate({ to: '/audit', search: {} });
              }}
            >
              Clear
            </Button>
          )}
        </div>
      </form>
      {query.isPending ? (
        <Spinner />
      ) : query.isError ? (
        <Alert>{errorMessage(query.error)}</Alert>
      ) : items.length === 0 ? (
        <EmptyState title="No entries match" />
      ) : (
        <>
          <ul className="space-y-2" aria-label="Audit entries">
            {items.map((item) => (
              <Entry key={item.id} item={item} />
            ))}
          </ul>
          {query.hasNextPage && (
            <div className="mt-4 flex justify-center">
              <Button loading={query.isFetchingNextPage} onClick={() => void query.fetchNextPage()}>
                Load older entries
              </Button>
            </div>
          )}
        </>
      )}
    </>
  );
}
