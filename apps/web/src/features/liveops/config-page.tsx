import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { call, client, errorMessage } from '../../api/client';
import type { ConfigEntry } from '../../api/types';
import { RequirePermission } from '../../app/permissions';
import { DataList } from '../../components/data-list';
import { Dialog } from '../../components/dialog';
import { ReasonDialog, ReasonField } from '../../components/reason-dialog';
import { useToast } from '../../components/toast';
import {
  Alert,
  Badge,
  Button,
  Code,
  EmptyState,
  Field,
  Input,
  PageHeader,
  Select,
  Spinner,
  Textarea,
} from '../../components/ui';
import { formatRelative } from '../../lib/format';

const CONFIG_KEY = ['config'] as const;
const KEY_PATTERN = /^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)+$/;

/** Parses the JSON editor value; returns an error message instead of throwing. */
export function parseJsonValue(text: string): { ok: true; value: unknown } | { ok: false; error: string } {
  if (text.trim() === '')
    return { ok: false, error: 'Enter a JSON value (e.g. true, 120, "text", {"a": 1}).' };
  try {
    return { ok: true, value: JSON.parse(text) };
  } catch (err) {
    return { ok: false, error: `Not valid JSON: ${(err as Error).message}` };
  }
}

export function ConfigPage() {
  return (
    <RequirePermission permission="liveops.config">
      <RemoteConfig />
    </RequirePermission>
  );
}

function RemoteConfig() {
  const config = useQuery({
    queryKey: CONFIG_KEY,
    queryFn: () => call(() => client.GET('/admin/v1/config')),
  });
  const [editing, setEditing] = useState<ConfigEntry | 'new' | null>(null);
  const [deleting, setDeleting] = useState<ConfigEntry | null>(null);
  const queryClient = useQueryClient();
  const toast = useToast();
  const items = config.data?.items ?? [];

  return (
    <>
      <PageHeader
        title="Remote config"
        description="Values delivered to the app at start-up (client), to game servers (server), or both. Changes apply within seconds; the app keeps built-in defaults for missing keys."
        actions={
          <Button
            variant="primary"
            icon={<Plus className="size-4" aria-hidden />}
            onClick={() => setEditing('new')}
          >
            New entry
          </Button>
        }
      />
      {config.isPending ? (
        <Spinner />
      ) : config.isError ? (
        <Alert>{errorMessage(config.error)}</Alert>
      ) : items.length === 0 ? (
        <EmptyState title="No config entries">
          Add a key such as feature.voiceChat to turn features on or off without an app update.
        </EmptyState>
      ) : (
        <DataList
          caption="Remote config entries"
          rows={items}
          rowKey={(e) => e.key}
          rowLabel={(e) => <Code>{e.key}</Code>}
          columns={[
            { header: 'Key', hideOnCard: true, cell: (e) => <Code>{e.key}</Code> },
            {
              header: 'Value',
              cell: (e) => <code className="font-mono text-xs break-all">{JSON.stringify(e.value)}</code>,
            },
            {
              header: 'Scope',
              cell: (e) => (
                <Badge tone={e.scope === 'server' ? 'info' : e.scope === 'both' ? 'accent' : 'neutral'}>
                  {e.scope}
                </Badge>
              ),
            },
            { header: 'Version', className: 'tabular-nums', cell: (e) => `v${e.version}` },
            { header: 'Updated', cell: (e) => formatRelative(e.updatedAt) },
          ]}
          rowActions={(e) => (
            <>
              <Button
                size="sm"
                aria-label={`Edit ${e.key}`}
                icon={<Pencil className="size-4" aria-hidden />}
                onClick={() => setEditing(e)}
              >
                Edit
              </Button>
              <Button
                size="sm"
                variant="ghost"
                aria-label={`Delete ${e.key}`}
                icon={<Trash2 className="size-4" aria-hidden />}
                onClick={() => setDeleting(e)}
              />
            </>
          )}
        />
      )}
      {editing && (
        <ConfigEditor entry={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />
      )}
      <ReasonDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        title={`Delete ${deleting?.key ?? ''}`}
        description="Clients fall back to their built-in default for this key."
        confirmLabel="Delete"
        danger
        onConfirm={async (reason) => {
          await call(() =>
            client.DELETE('/admin/v1/config/{key}', {
              params: { path: { key: deleting!.key } },
              body: { reason },
            }),
          );
          await queryClient.invalidateQueries({ queryKey: CONFIG_KEY });
          toast.show('Config entry deleted');
        }}
      />
    </>
  );
}

function ConfigEditor({ entry, onClose }: { entry: ConfigEntry | null; onClose: () => void }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [key, setKey] = useState(entry?.key ?? '');
  const [scope, setScope] = useState<ConfigEntry['scope']>(entry?.scope ?? 'client');
  const [value, setValue] = useState(entry ? JSON.stringify(entry.value, null, 2) : '');
  const [description, setDescription] = useState(entry?.description ?? '');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const parsed = parseJsonValue(value);
  const keyError = key && !KEY_PATTERN.test(key) ? 'Use dotted camelCase, e.g. feature.voiceChat' : null;

  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title={entry ? `Edit ${entry.key}` : 'New config entry'}
      onSubmit={async () => {
        if (!parsed.ok || keyError) return;
        setBusy(true);
        setError(null);
        try {
          await call(() =>
            client.PUT('/admin/v1/config/{key}', {
              params: { path: { key } },
              body: {
                value: parsed.value,
                scope,
                reason: reason.trim(),
                ...(description.trim() ? { description: description.trim() } : {}),
              },
            }),
          );
          await queryClient.invalidateQueries({ queryKey: CONFIG_KEY });
          toast.show(`${key} saved`);
          onClose();
        } catch (err) {
          setError(errorMessage(err));
        } finally {
          setBusy(false);
        }
      }}
      footer={
        <>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" loading={busy} disabled={!parsed.ok || Boolean(keyError)}>
            Save
          </Button>
        </>
      }
    >
      <div className="grid gap-4 sm:grid-cols-[1fr_10rem]">
        <Field label="Key" error={keyError}>
          {(p) => (
            <Input
              {...p}
              required
              disabled={entry !== null}
              value={key}
              onChange={(e) => setKey(e.target.value)}
              placeholder="feature.voiceChat"
              className="font-mono"
            />
          )}
        </Field>
        <Field label="Scope">
          {(p) => (
            <Select {...p} value={scope} onChange={(e) => setScope(e.target.value as ConfigEntry['scope'])}>
              <option value="client">client</option>
              <option value="server">server</option>
              <option value="both">both</option>
            </Select>
          )}
        </Field>
      </div>
      <Field
        label="Value (JSON)"
        error={value && !parsed.ok ? parsed.error : null}
        hint={'true / false, numbers, "strings", arrays or objects. Max 16 KB.'}
      >
        {(p) => (
          <Textarea
            {...p}
            rows={6}
            required
            value={value}
            onChange={(e) => setValue(e.target.value)}
            className="font-mono text-sm"
            spellCheck={false}
          />
        )}
      </Field>
      <Field label="Description (optional)">
        {(p) => (
          <Input
            {...p}
            maxLength={500}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
        )}
      </Field>
      <ReasonField value={reason} onChange={setReason} />
      {error && <Alert>{error}</Alert>}
    </Dialog>
  );
}
