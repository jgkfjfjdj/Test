import { useQueryClient } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { useState } from 'react';
import { call, client, errorMessage } from '../../api/client';
import type { MaintenanceScope, MaintenanceWindow } from '../../api/types';
import { RequirePermission } from '../../app/permissions';
import { DataList } from '../../components/data-list';
import { Dialog } from '../../components/dialog';
import { ReasonDialog, ReasonField } from '../../components/reason-dialog';
import { useToast } from '../../components/toast';
import {
  Alert,
  Badge,
  Button,
  EmptyState,
  Field,
  Input,
  PageHeader,
  Select,
  Spinner,
  Textarea,
} from '../../components/ui';
import { formatDateTime, localInputToIso } from '../../lib/format';
import { MAINTENANCE_KEY, useMaintenance, windowState, type WindowState } from './maintenance-queries';

const SCOPES: { value: MaintenanceScope; label: string; effect: string }[] = [
  { value: 'full', label: 'Full', effect: 'Blocks sign-in, joining servers and the store.' },
  {
    value: 'login',
    label: 'Sign-in',
    effect: 'Blocks sign-in and session refresh. Players in game stay connected.',
  },
  { value: 'matchmaking', label: 'Joining servers', effect: 'Blocks new join tickets. Sign-in still works.' },
  { value: 'store', label: 'Store', effect: 'Blocks purchases (when the store exists).' },
];

const stateTone: Record<WindowState, 'warn' | 'info' | 'neutral'> = {
  active: 'warn',
  scheduled: 'info',
  ended: 'neutral',
  cancelled: 'neutral',
};

export function MaintenancePage() {
  return (
    <RequirePermission permission="liveops.maintenance">
      <Maintenance />
    </RequirePermission>
  );
}

function Maintenance() {
  const windows = useMaintenance();
  const [creating, setCreating] = useState(false);
  const [cancelling, setCancelling] = useState<MaintenanceWindow | null>(null);
  const queryClient = useQueryClient();
  const toast = useToast();
  const items = windows.data?.items ?? [];

  return (
    <>
      <PageHeader
        title="Maintenance"
        description="Players see upcoming windows 24 hours ahead in the app, and a blocking screen while a window is active."
        actions={
          <Button
            variant="primary"
            icon={<Plus className="size-4" aria-hidden />}
            onClick={() => setCreating(true)}
          >
            Schedule maintenance
          </Button>
        }
      />
      {windows.isPending ? (
        <Spinner />
      ) : windows.isError ? (
        <Alert>{errorMessage(windows.error)}</Alert>
      ) : items.length === 0 ? (
        <EmptyState title="No maintenance windows yet" />
      ) : (
        <DataList
          caption="Maintenance windows"
          rows={items}
          rowKey={(w) => w.id}
          rowLabel={(w) => SCOPES.find((s) => s.value === w.scope)?.label ?? w.scope}
          columns={[
            {
              header: 'Scope',
              hideOnCard: true,
              cell: (w) => (
                <span className="font-medium">
                  {SCOPES.find((s) => s.value === w.scope)?.label ?? w.scope}
                </span>
              ),
            },
            {
              header: 'State',
              cell: (w) => {
                const state = windowState(w);
                return <Badge tone={stateTone[state]}>{state}</Badge>;
              },
            },
            { header: 'Starts', cell: (w) => formatDateTime(w.startsAt) },
            { header: 'Ends', cell: (w) => (w.endsAt ? formatDateTime(w.endsAt) : 'Until cancelled') },
            { header: 'Message', cell: (w) => <span className="line-clamp-2">{w.message['en']}</span> },
          ]}
          rowActions={(w) =>
            ['active', 'scheduled'].includes(windowState(w)) ? (
              <Button size="sm" onClick={() => setCancelling(w)}>
                {windowState(w) === 'active' ? 'End now' : 'Cancel'}
              </Button>
            ) : null
          }
        />
      )}
      <CreateMaintenanceDialog open={creating} onClose={() => setCreating(false)} />
      <ReasonDialog
        open={cancelling !== null}
        onClose={() => setCancelling(null)}
        title={
          cancelling && windowState(cancelling) === 'active'
            ? 'End maintenance now'
            : 'Cancel scheduled maintenance'
        }
        confirmLabel="Confirm"
        onConfirm={async (reason) => {
          await call(() =>
            client.POST('/admin/v1/maintenance/{id}/cancel', {
              params: { path: { id: cancelling!.id } },
              body: { reason },
            }),
          );
          await queryClient.invalidateQueries({ queryKey: MAINTENANCE_KEY });
          toast.show('Maintenance window closed');
        }}
      />
    </>
  );
}

function CreateMaintenanceDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [scope, setScope] = useState<MaintenanceScope>('full');
  const [startNow, setStartNow] = useState(true);
  const [startsAt, setStartsAt] = useState('');
  const [endsAt, setEndsAt] = useState('');
  const [message, setMessage] = useState('');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const effect = SCOPES.find((s) => s.value === scope)?.effect;

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Schedule maintenance"
      onSubmit={async () => {
        setBusy(true);
        setError(null);
        try {
          const start = startNow ? undefined : localInputToIso(startsAt);
          const end = localInputToIso(endsAt);
          await call(() =>
            client.POST('/admin/v1/maintenance', {
              body: {
                scope,
                message: { en: message.trim() },
                reason: reason.trim(),
                ...(start ? { startsAt: start } : {}),
                ...(end ? { endsAt: end } : {}),
              },
            }),
          );
          await queryClient.invalidateQueries({ queryKey: MAINTENANCE_KEY });
          toast.show(startNow ? 'Maintenance started' : 'Maintenance scheduled');
          setMessage('');
          setReason('');
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
          <Button type="submit" variant="primary" loading={busy}>
            {startNow ? 'Start now' : 'Schedule'}
          </Button>
        </>
      }
    >
      <Field label="What is affected" hint={effect}>
        {(p) => (
          <Select {...p} value={scope} onChange={(e) => setScope(e.target.value as MaintenanceScope)}>
            {SCOPES.map((s) => (
              <option key={s.value} value={s.value}>
                {s.label}
              </option>
            ))}
          </Select>
        )}
      </Field>
      <fieldset className="space-y-2">
        <legend className="text-sm font-medium">Start</legend>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="radio"
            name="start"
            checked={startNow}
            onChange={() => setStartNow(true)}
            className="size-4 accent-[var(--color-accent)]"
          />{' '}
          Immediately
        </label>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="radio"
            name="start"
            checked={!startNow}
            onChange={() => setStartNow(false)}
            className="size-4 accent-[var(--color-accent)]"
          />{' '}
          At a set time
        </label>
        {!startNow && (
          <Input
            type="datetime-local"
            aria-label="Start time"
            required
            value={startsAt}
            onChange={(e) => setStartsAt(e.target.value)}
          />
        )}
      </fieldset>
      <Field label="End (optional)" hint="Leave empty to end it manually.">
        {(p) => (
          <Input {...p} type="datetime-local" value={endsAt} onChange={(e) => setEndsAt(e.target.value)} />
        )}
      </Field>
      <Field label="Message to players (English)">
        {(p) => (
          <Textarea
            {...p}
            required
            maxLength={500}
            rows={3}
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            placeholder="We're upgrading servers. Back in about 30 minutes."
          />
        )}
      </Field>
      <ReasonField value={reason} onChange={setReason} />
      {error && <Alert>{error}</Alert>}
    </Dialog>
  );
}
