import { useQueryClient } from '@tanstack/react-query';
import { AlertTriangle } from 'lucide-react';
import { useState } from 'react';
import { call, client, errorMessage } from '../../api/client';
import type { CreatedServer } from '../../api/types';
import { CopyField } from '../../components/copy-field';
import { Dialog } from '../../components/dialog';
import { ReasonField } from '../../components/reason-dialog';
import { Alert, Button, Field, Input, Select } from '../../components/ui';
import { SERVERS_KEY } from './queries';

const REGIONS = [
  'eu-west',
  'eu-central',
  'na-east',
  'na-west',
  'sa-east',
  'ap-southeast',
  'ap-northeast',
  'oc-east',
];

/** Registers an official server and shows its token exactly once. */
export function CreateServerDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [form, setForm] = useState({
    name: '',
    region: 'eu-west',
    maxPlayers: 50,
    mode: 'pvp' as 'pvp' | 'pve',
    tags: 'official',
    reason: '',
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [created, setCreated] = useState<CreatedServer | null>(null);

  const close = () => {
    setCreated(null);
    setError(null);
    setForm((f) => ({ ...f, name: '', reason: '' }));
    onClose();
  };

  if (created) {
    return (
      <Dialog
        open={open}
        onClose={close}
        title="Server registered"
        description={`${created.server.name} (${created.server.region})`}
        footer={
          <Button variant="primary" onClick={close}>
            I've stored the token
          </Button>
        }
      >
        <Alert tone="warn" title="Shown only once">
          <span className="inline-flex items-start gap-2">
            <AlertTriangle className="mt-0.5 size-4 shrink-0" aria-hidden />
            Copy the token into the server's configuration now (KINDLING_SERVER_TOKEN). It cannot be shown
            again — if it's lost, rotate it.
          </span>
        </Alert>
        <CopyField label="Server token" value={created.token} secret />
        <CopyField label="Server id" value={created.server.id} />
      </Dialog>
    );
  }

  return (
    <Dialog
      open={open}
      onClose={close}
      title="Register an official server"
      description="Creates the server record and a secret token the game server uses to connect."
      onSubmit={async () => {
        setBusy(true);
        setError(null);
        try {
          const tags = form.tags
            .split(',')
            .map((t) => t.trim())
            .filter(Boolean);
          const result = await call(() =>
            client.POST('/admin/v1/servers', {
              body: {
                name: form.name.trim(),
                region: form.region,
                maxPlayers: form.maxPlayers,
                mode: form.mode,
                tags,
                reason: form.reason.trim(),
              },
            }),
          );
          setCreated(result);
          await queryClient.invalidateQueries({ queryKey: SERVERS_KEY });
        } catch (err) {
          setError(errorMessage(err));
        } finally {
          setBusy(false);
        }
      }}
      footer={
        <>
          <Button onClick={close}>Cancel</Button>
          <Button type="submit" variant="primary" loading={busy}>
            Register server
          </Button>
        </>
      }
    >
      <Field label="Name" hint="Shown in the server browser.">
        {(p) => (
          <Input
            {...p}
            required
            minLength={3}
            maxLength={64}
            value={form.name}
            onChange={(e) => setForm({ ...form, name: e.target.value })}
            placeholder="Official EU #1"
          />
        )}
      </Field>
      <div className="grid gap-4 sm:grid-cols-3">
        <Field label="Region">
          {(p) => (
            <Select {...p} value={form.region} onChange={(e) => setForm({ ...form, region: e.target.value })}>
              {REGIONS.map((r) => (
                <option key={r}>{r}</option>
              ))}
            </Select>
          )}
        </Field>
        <Field label="Mode">
          {(p) => (
            <Select
              {...p}
              value={form.mode}
              onChange={(e) => setForm({ ...form, mode: e.target.value as 'pvp' | 'pve' })}
            >
              <option value="pvp">PvP</option>
              <option value="pve">PvE</option>
            </Select>
          )}
        </Field>
        <Field label="Max players">
          {(p) => (
            <Input
              {...p}
              type="number"
              min={1}
              max={200}
              required
              value={form.maxPlayers}
              onChange={(e) => setForm({ ...form, maxPlayers: Number(e.target.value) })}
            />
          )}
        </Field>
      </div>
      <Field label="Tags" hint="Comma-separated, e.g. official, biweekly, 2x.">
        {(p) => (
          <Input {...p} value={form.tags} onChange={(e) => setForm({ ...form, tags: e.target.value })} />
        )}
      </Field>
      <ReasonField value={form.reason} onChange={(reason) => setForm({ ...form, reason })} />
      {error && <Alert>{error}</Alert>}
    </Dialog>
  );
}
