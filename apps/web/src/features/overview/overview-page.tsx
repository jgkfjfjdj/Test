import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { ArrowRight, Server, Users, Wrench } from 'lucide-react';
import type { ReactNode } from 'react';
import { call, client } from '../../api/client';
import { useSession } from '../../app/session';
import { Alert, Card, KeyValue, PageHeader, Spinner } from '../../components/ui';
import { formatDateTime, formatDuration, formatNumber } from '../../lib/format';
import { useMaintenance, windowState } from '../liveops/maintenance-queries';
import { livePlayers } from '../servers/server-status';
import { useServers } from '../servers/queries';

function Stat({
  label,
  value,
  icon,
  to,
  detail,
}: {
  label: string;
  value: ReactNode;
  icon: ReactNode;
  to?: '/servers' | '/maintenance';
  detail?: ReactNode;
}) {
  const body = (
    <div className="flex h-full flex-col justify-between gap-3 rounded-xl border border-line bg-surface p-4 transition-colors hover:border-accent/50">
      <div className="flex items-center justify-between text-sm text-muted">
        <span>{label}</span>
        {icon}
      </div>
      <div>
        <div className="text-3xl font-semibold tabular-nums">{value}</div>
        {detail && <div className="mt-1 text-sm text-muted">{detail}</div>}
      </div>
    </div>
  );
  return to ? (
    <Link to={to} className="block rounded-xl">
      {body}
    </Link>
  ) : (
    body
  );
}

export function OverviewPage() {
  const { staff, can } = useSession();
  const servers = useServers(can('servers.read'));
  const maintenance = useMaintenance(can('liveops.maintenance'));
  const info = useQuery({
    queryKey: ['build-info'],
    queryFn: () => call(() => client.GET('/health/info')),
    staleTime: 60_000,
  });

  const items = servers.data?.items ?? [];
  const online = items.filter((s) => s.online);
  const players = online.reduce((sum, s) => sum + livePlayers(s).players, 0);
  const active = (maintenance.data?.items ?? []).filter((w) => windowState(w) === 'active');

  return (
    <>
      <PageHeader
        title={`Hello, ${staff?.name.split(' ')[0] ?? ''}`}
        description="Live state of the platform. Numbers refresh every 10 seconds."
      />

      {active.length > 0 && (
        <div className="mb-5">
          <Alert tone="warn" title="Maintenance in progress">
            {active.map((w) => `${w.scope}: ${w.message['en'] ?? ''}`).join(' · ')}{' '}
            <Link to="/maintenance" className="font-medium underline">
              Manage
            </Link>
          </Alert>
        </div>
      )}

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {can('servers.read') && (
          <>
            <Stat
              label="Servers online"
              icon={<Server className="size-4" aria-hidden />}
              to="/servers"
              value={servers.isPending ? '…' : `${online.length} / ${items.length}`}
              detail="registered servers with a live link"
            />
            <Stat
              label="Players in game"
              icon={<Users className="size-4" aria-hidden />}
              value={servers.isPending ? '…' : formatNumber(players)}
              detail="across all online servers"
            />
          </>
        )}
        {can('liveops.maintenance') && (
          <Stat
            label="Maintenance"
            icon={<Wrench className="size-4" aria-hidden />}
            to="/maintenance"
            value={maintenance.isPending ? '…' : active.length === 0 ? 'None' : `${active.length} active`}
            detail={`${(maintenance.data?.items ?? []).filter((w) => windowState(w) === 'scheduled').length} scheduled`}
          />
        )}
      </div>

      {can('servers.read') && online.length > 0 && (
        <Card
          className="mt-5"
          title="Busiest servers"
          actions={
            <Link to="/servers" className="inline-flex items-center gap-1 text-sm font-medium text-accent">
              All servers <ArrowRight className="size-4" aria-hidden />
            </Link>
          }
        >
          <ul className="divide-y divide-line">
            {[...online]
              .sort((a, b) => livePlayers(b).players - livePlayers(a).players)
              .slice(0, 5)
              .map((s) => {
                const { players: p, max } = livePlayers(s);
                const live = s.live as { tickMsP99?: number; uptimeSeconds?: number } | null;
                return (
                  <li key={s.id} className="flex items-center justify-between gap-3 py-2.5 text-sm">
                    <Link
                      to="/servers/$serverId"
                      params={{ serverId: s.id }}
                      className="truncate font-medium hover:underline"
                    >
                      {s.name}
                    </Link>
                    <span className="shrink-0 text-muted tabular-nums">
                      {p}/{max} · p99 {formatNumber(live?.tickMsP99 ?? 0, 1)} ms · up{' '}
                      {formatDuration(live?.uptimeSeconds ?? 0)}
                    </span>
                  </li>
                );
              })}
          </ul>
        </Card>
      )}

      <Card className="mt-5" title="Backend">
        {info.isPending ? (
          <Spinner />
        ) : info.data ? (
          <KeyValue
            items={[
              { label: 'Environment', value: info.data.environment },
              { label: 'Version', value: info.data.version },
              { label: 'Commit', value: info.data.commit },
              { label: 'Running since', value: formatDateTime(info.data.startedAt) },
            ]}
          />
        ) : (
          <Alert>Could not load build information.</Alert>
        )}
      </Card>
    </>
  );
}
