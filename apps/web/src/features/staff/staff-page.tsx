import { useQuery, useQueryClient } from '@tanstack/react-query';
import { MoreHorizontal, UserPlus } from 'lucide-react';
import { useState } from 'react';
import { call, client, errorMessage } from '../../api/client';
import type { RolesResponse, StaffMember } from '../../api/types';
import { RequirePermission } from '../../app/permissions';
import { useSession } from '../../app/session';
import { CopyField } from '../../components/copy-field';
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
} from '../../components/ui';
import { formatRelative } from '../../lib/format';

const STAFF_KEY = ['staff'] as const;
const ROLES_KEY = ['roles'] as const;

const ROLE_HELP: Record<string, string> = {
  super_admin: 'Everything, including managing staff.',
  admin: 'Everything except managing staff.',
  moderator: 'Reports, sanctions, text filters, kick/broadcast.',
  support: 'Player lookups and fixes, cosmetic grants.',
  liveops: 'Remote config, events, seasons, announcements.',
  analyst: 'Read-only players, servers and analytics.',
  server_ops: 'Servers, builds, app versions, maintenance, DB monitoring.',
};

const statusTone = { active: 'ok', invited: 'info', disabled: 'danger' } as const;

export function StaffPage() {
  return (
    <RequirePermission permission="staff.manage">
      <Staff />
    </RequirePermission>
  );
}

function RolePicker({
  roles,
  value,
  onChange,
}: {
  roles: RolesResponse['roles'];
  value: string[];
  onChange: (v: string[]) => void;
}) {
  return (
    <fieldset>
      <legend className="mb-1.5 text-sm font-medium">Roles</legend>
      <div className="space-y-1">
        {roles.map((role) => (
          <label
            key={role.id}
            className="flex min-h-11 cursor-pointer items-start gap-3 rounded-lg px-2 py-2 hover:bg-surface-2"
          >
            <input
              type="checkbox"
              className="mt-0.5 size-4 accent-[var(--color-accent)]"
              checked={value.includes(role.id)}
              onChange={(e) =>
                onChange(e.target.checked ? [...value, role.id] : value.filter((r) => r !== role.id))
              }
            />
            <span>
              <span className="block text-sm font-medium">{role.name}</span>
              <span className="block text-xs text-muted">
                {ROLE_HELP[role.id] ?? `${role.permissions.length} permissions`}
              </span>
            </span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}

function Staff() {
  const { staff: me } = useSession();
  const list = useQuery({ queryKey: STAFF_KEY, queryFn: () => call(() => client.GET('/admin/v1/staff')) });
  const roles = useQuery({
    queryKey: ROLES_KEY,
    queryFn: () => call(() => client.GET('/admin/v1/staff/roles')),
    staleTime: 300_000,
  });
  const [inviting, setInviting] = useState(false);
  const [editing, setEditing] = useState<StaffMember | null>(null);
  const items = list.data?.items ?? [];

  return (
    <>
      <PageHeader
        title="Staff"
        description="Staff accounts are separate from player accounts. Everyone signs in with a password and an authenticator app."
        actions={
          <Button
            variant="primary"
            icon={<UserPlus className="size-4" aria-hidden />}
            onClick={() => setInviting(true)}
            disabled={!roles.data}
          >
            Invite staff
          </Button>
        }
      />
      {list.isPending ? (
        <Spinner />
      ) : list.isError ? (
        <Alert>{errorMessage(list.error)}</Alert>
      ) : items.length === 0 ? (
        <EmptyState title="No staff yet" />
      ) : (
        <DataList
          caption="Staff members"
          rows={items}
          rowKey={(s) => s.id}
          rowLabel={(s) => s.name}
          columns={[
            {
              header: 'Name',
              hideOnCard: true,
              cell: (s) => (
                <div className="min-w-0">
                  <div className="font-medium">
                    {s.name} {s.id === me?.id && <Badge tone="accent">you</Badge>}
                  </div>
                  <div className="truncate text-xs text-muted">{s.email}</div>
                </div>
              ),
            },
            {
              header: 'Email',
              cell: (s) => <span className="truncate md:hidden">{s.email}</span>,
              className: 'md:hidden',
            },
            {
              header: 'Roles',
              cell: (s) => (
                <span className="flex flex-wrap gap-1">
                  {s.roles.length ? s.roles.map((r) => <Badge key={r}>{r.replace('_', ' ')}</Badge>) : '—'}
                </span>
              ),
            },
            {
              header: 'Status',
              cell: (s) => (
                <Badge tone={statusTone[s.status as keyof typeof statusTone] ?? 'neutral'}>{s.status}</Badge>
              ),
            },
            { header: 'Last sign-in', cell: (s) => formatRelative(s.lastLoginAt) },
          ]}
          rowActions={(s) =>
            s.id === me?.id ? null : (
              <Button
                size="sm"
                aria-label={`Manage ${s.name}`}
                icon={<MoreHorizontal className="size-4" aria-hidden />}
                onClick={() => setEditing(s)}
              >
                Manage
              </Button>
            )
          }
        />
      )}
      {roles.data && (
        <InviteDialog open={inviting} onClose={() => setInviting(false)} roles={roles.data.roles} />
      )}
      {editing && roles.data && (
        <ManageStaffDialog member={editing} roles={roles.data.roles} onClose={() => setEditing(null)} />
      )}
    </>
  );
}

function InviteDialog({
  open,
  onClose,
  roles,
}: {
  open: boolean;
  onClose: () => void;
  roles: RolesResponse['roles'];
}) {
  const queryClient = useQueryClient();
  const [email, setEmail] = useState('');
  const [name, setName] = useState('');
  const [picked, setPicked] = useState<string[]>([]);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [setupUrl, setSetupUrl] = useState<string | null>(null);
  const close = () => {
    setEmail('');
    setName('');
    setPicked([]);
    setReason('');
    setError(null);
    setSetupUrl(null);
    onClose();
  };

  if (setupUrl) {
    return (
      <Dialog
        open={open}
        onClose={close}
        title="Invitation sent"
        footer={
          <Button variant="primary" onClick={close}>
            Done
          </Button>
        }
      >
        <p className="text-sm">
          We emailed <strong>{email}</strong> a setup link (valid for 72 hours). If email doesn't arrive,
          share this link privately:
        </p>
        <CopyField label="Setup link" value={setupUrl} secret />
      </Dialog>
    );
  }
  return (
    <Dialog
      open={open}
      onClose={close}
      title="Invite a staff member"
      onSubmit={async () => {
        setBusy(true);
        setError(null);
        try {
          const res = await call(() =>
            client.POST('/admin/v1/staff', {
              body: { email: email.trim(), name: name.trim(), roles: picked, reason: reason.trim() },
            }),
          );
          setSetupUrl(res.setupUrl);
          await queryClient.invalidateQueries({ queryKey: STAFF_KEY });
        } catch (err) {
          setError(errorMessage(err));
        } finally {
          setBusy(false);
        }
      }}
      footer={
        <>
          <Button onClick={close}>Cancel</Button>
          <Button type="submit" variant="primary" loading={busy} disabled={picked.length === 0}>
            Send invitation
          </Button>
        </>
      }
    >
      <Field label="Name">
        {(p) => (
          <Input {...p} required maxLength={100} value={name} onChange={(e) => setName(e.target.value)} />
        )}
      </Field>
      <Field label="Email">
        {(p) => (
          <Input {...p} type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        )}
      </Field>
      <RolePicker roles={roles} value={picked} onChange={setPicked} />
      <ReasonField value={reason} onChange={setReason} />
      {error && <Alert>{error}</Alert>}
    </Dialog>
  );
}

function ManageStaffDialog({
  member,
  roles,
  onClose,
}: {
  member: StaffMember;
  roles: RolesResponse['roles'];
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [picked, setPicked] = useState<string[]>(member.roles);
  const [status, setStatus] = useState(member.status === 'disabled' ? 'disabled' : 'active');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [action, setAction] = useState<'revoke' | 'reset' | null>(null);
  const [setupUrl, setSetupUrl] = useState<string | null>(null);
  const refresh = () => queryClient.invalidateQueries({ queryKey: STAFF_KEY });

  const rolesChanged = [...picked].sort().join() !== [...member.roles].sort().join();
  const statusChanged = member.status !== 'invited' && status !== member.status;

  return (
    <>
      <Dialog
        open={action === null}
        onClose={onClose}
        title={`Manage ${member.name}`}
        description={member.email}
        size="lg"
        onSubmit={async () => {
          setBusy(true);
          setError(null);
          try {
            await call(() =>
              client.PATCH('/admin/v1/staff/{id}', {
                params: { path: { id: member.id } },
                body: {
                  reason: reason.trim(),
                  ...(rolesChanged ? { roles: picked } : {}),
                  ...(statusChanged ? { status: status as 'active' | 'disabled' } : {}),
                },
              }),
            );
            await refresh();
            toast.show(`${member.name} updated`);
            onClose();
          } catch (err) {
            setError(errorMessage(err));
          } finally {
            setBusy(false);
          }
        }}
        footer={
          <>
            <Button onClick={onClose}>Close</Button>
            <Button type="submit" variant="primary" loading={busy} disabled={!rolesChanged && !statusChanged}>
              Save changes
            </Button>
          </>
        }
      >
        {setupUrl && (
          <div className="space-y-2">
            <Alert tone="ok">Credentials reset. A new setup link was emailed.</Alert>
            <CopyField label="New setup link" value={setupUrl} secret />
          </div>
        )}
        <RolePicker roles={roles} value={picked} onChange={setPicked} />
        {member.status !== 'invited' && (
          <Field label="Status" hint="Disabling signs the person out everywhere.">
            {(p) => (
              <Select {...p} value={status} onChange={(e) => setStatus(e.target.value)}>
                <option value="active">Active</option>
                <option value="disabled">Disabled</option>
              </Select>
            )}
          </Field>
        )}
        <ReasonField value={reason} onChange={setReason} />
        {error && <Alert>{error}</Alert>}
        <div className="flex flex-wrap gap-2 border-t border-line pt-4">
          <Button size="sm" onClick={() => setAction('revoke')}>
            Sign out everywhere
          </Button>
          <Button size="sm" variant="danger" onClick={() => setAction('reset')}>
            Reset password &amp; authenticator
          </Button>
        </div>
      </Dialog>
      <ReasonDialog
        open={action === 'revoke'}
        onClose={() => setAction(null)}
        title={`Sign ${member.name} out everywhere`}
        confirmLabel="Sign out"
        onConfirm={async (r) => {
          const res = await call(() =>
            client.POST('/admin/v1/staff/{id}/sessions/revoke', {
              params: { path: { id: member.id } },
              body: { reason: r },
            }),
          );
          toast.show(`${res.revoked} session(s) ended`);
        }}
      />
      <ReasonDialog
        open={action === 'reset'}
        onClose={() => setAction(null)}
        title={`Reset ${member.name}'s credentials`}
        description="Their password and authenticator are removed and they get a new setup link. All their sessions end."
        confirmLabel="Reset credentials"
        danger
        onConfirm={async (r) => {
          const res = await call(() =>
            client.POST('/admin/v1/staff/{id}/reset-credentials', {
              params: { path: { id: member.id } },
              body: { reason: r },
            }),
          );
          setSetupUrl(res.setupUrl);
          await refresh();
        }}
      />
    </>
  );
}
