import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil } from 'lucide-react';
import { useState } from 'react';
import { call, client, errorMessage } from '../../api/client';
import type { VersionPolicy } from '../../api/types';
import { RequirePermission } from '../../app/permissions';
import { Dialog } from '../../components/dialog';
import { ReasonField } from '../../components/reason-dialog';
import { useToast } from '../../components/toast';
import {
  Alert,
  Button,
  Card,
  EmptyState,
  Field,
  Input,
  KeyValue,
  PageHeader,
  Spinner,
} from '../../components/ui';
import { formatRelative } from '../../lib/format';

const VERSIONS_KEY = ['version-policy'] as const;

export function VersionsPage() {
  return (
    <RequirePermission permission="versions.manage">
      <Versions />
    </RequirePermission>
  );
}

function Versions() {
  const policies = useQuery({
    queryKey: VERSIONS_KEY,
    queryFn: () => call(() => client.GET('/admin/v1/version-policy')),
  });
  const [editing, setEditing] = useState<VersionPolicy | null>(null);
  return (
    <>
      <PageHeader
        title="App versions"
        description="Builds below the minimum must update before playing (blocking screen with an App Store button). Builds below the latest see an optional update prompt."
      />
      {policies.isPending ? (
        <Spinner />
      ) : policies.isError ? (
        <Alert>{errorMessage(policies.error)}</Alert>
      ) : (policies.data?.items ?? []).length === 0 ? (
        <EmptyState title="No version policy" />
      ) : (
        <div className="space-y-4">
          {policies.data!.items.map((p) => (
            <Card
              key={p.platform}
              title={p.platform === 'ios' ? 'iOS' : p.platform}
              description={`Updated ${formatRelative(p.updatedAt)}`}
              actions={
                <Button
                  size="sm"
                  icon={<Pencil className="size-4" aria-hidden />}
                  onClick={() => setEditing(p)}
                >
                  Edit
                </Button>
              }
            >
              <KeyValue
                items={[
                  { label: 'Minimum build', value: p.minSupportedBuild },
                  { label: 'Latest build', value: p.latestBuild },
                  {
                    label: 'Store URL',
                    value: (
                      <a href={p.storeUrl} className="text-accent underline" rel="noreferrer" target="_blank">
                        {p.storeUrl}
                      </a>
                    ),
                  },
                ]}
              />
            </Card>
          ))}
        </div>
      )}
      {editing && <EditPolicy policy={editing} onClose={() => setEditing(null)} />}
    </>
  );
}

function EditPolicy({ policy, onClose }: { policy: VersionPolicy; onClose: () => void }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [min, setMin] = useState(policy.minSupportedBuild);
  const [latest, setLatest] = useState(policy.latestBuild);
  const [storeUrl, setStoreUrl] = useState(policy.storeUrl);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const invalid = min > latest;
  return (
    <Dialog
      open
      onClose={onClose}
      title={`Edit ${policy.platform === 'ios' ? 'iOS' : policy.platform} version policy`}
      description="Raising the minimum forces older builds to update immediately. Check adoption first."
      onSubmit={async () => {
        if (invalid) return;
        setBusy(true);
        setError(null);
        try {
          await call(() =>
            client.PUT('/admin/v1/version-policy/{platform}', {
              params: { path: { platform: policy.platform } },
              body: { minSupportedBuild: min, latestBuild: latest, storeUrl, reason: reason.trim() },
            }),
          );
          await queryClient.invalidateQueries({ queryKey: VERSIONS_KEY });
          toast.show('Version policy updated');
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
          <Button type="submit" variant="primary" loading={busy} disabled={invalid}>
            Save
          </Button>
        </>
      }
    >
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Minimum supported build" error={invalid ? 'Cannot be above the latest build.' : null}>
          {(p) => (
            <Input
              {...p}
              type="number"
              min={0}
              required
              value={min}
              onChange={(e) => setMin(Number(e.target.value))}
            />
          )}
        </Field>
        <Field label="Latest build">
          {(p) => (
            <Input
              {...p}
              type="number"
              min={0}
              required
              value={latest}
              onChange={(e) => setLatest(Number(e.target.value))}
            />
          )}
        </Field>
      </div>
      <Field label="App Store URL">
        {(p) => (
          <Input
            {...p}
            type="url"
            pattern="https://.*"
            required
            value={storeUrl}
            onChange={(e) => setStoreUrl(e.target.value)}
          />
        )}
      </Field>
      <ReasonField value={reason} onChange={setReason} />
      {error && <Alert>{error}</Alert>}
    </Dialog>
  );
}
