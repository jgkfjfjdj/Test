import { Link } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { useState } from 'react';
import { errorMessage } from '../../api/client';
import { RequirePermission } from '../../app/permissions';
import { useSession } from '../../app/session';
import { DataList } from '../../components/data-list';
import { Alert, Button, EmptyState, PageHeader, Spinner } from '../../components/ui';
import { formatRelative } from '../../lib/format';
import { CreateServerDialog } from './create-server-dialog';
import { useServers } from './queries';
import { ListingBadge, livePlayers, OnlineBadge } from './server-status';

export function ServersPage() {
  return (
    <RequirePermission permission="servers.read">
      <ServersList />
    </RequirePermission>
  );
}

function ServersList() {
  const { can } = useSession();
  const servers = useServers();
  const [creating, setCreating] = useState(false);
  const items = servers.data?.items ?? [];

  return (
    <>
      <PageHeader
        title="Servers"
        description="Official and community servers known to the platform. Live data comes from each server's link heartbeat (every 10 s)."
        actions={
          can('servers.manage') && (
            <Button
              variant="primary"
              icon={<Plus className="size-4" aria-hidden />}
              onClick={() => setCreating(true)}
            >
              Register server
            </Button>
          )
        }
      />
      {servers.isPending ? (
        <Spinner />
      ) : servers.isError ? (
        <Alert>{errorMessage(servers.error)}</Alert>
      ) : items.length === 0 ? (
        <EmptyState title="No servers registered yet">
          Register an official server to get its connection token.
        </EmptyState>
      ) : (
        <DataList
          caption="Servers"
          rows={items}
          rowKey={(s) => s.id}
          rowLabel={(s) => (
            <Link to="/servers/$serverId" params={{ serverId: s.id }} className="hover:underline">
              {s.name}
            </Link>
          )}
          columns={[
            {
              header: 'Name',
              hideOnCard: true,
              cell: (s) => (
                <Link
                  to="/servers/$serverId"
                  params={{ serverId: s.id }}
                  className="font-medium hover:underline"
                >
                  {s.name}
                </Link>
              ),
            },
            { header: 'Status', cell: (s) => <OnlineBadge server={s} /> },
            {
              header: 'Players',
              className: 'tabular-nums',
              cell: (s) => {
                const { players, max } = livePlayers(s);
                return s.online ? `${players} / ${max}` : `— / ${max}`;
              },
            },
            { header: 'Region', cell: (s) => s.region },
            { header: 'Listing', cell: (s) => <ListingBadge listing={s.listing} /> },
            {
              header: 'Build',
              cell: (s) => <span className="font-mono text-xs">{s.buildVersion ?? '—'}</span>,
            },
            { header: 'Last seen', cell: (s) => (s.online ? 'now' : formatRelative(s.lastSeenAt)) },
          ]}
        />
      )}
      <CreateServerDialog open={creating} onClose={() => setCreating(false)} />
    </>
  );
}
