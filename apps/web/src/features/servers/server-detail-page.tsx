import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { ArrowLeft, KeyRound, Send } from 'lucide-react';
import { useState } from 'react';
import { call, client, errorMessage } from '../../api/client';
import type { AdminServer, CommandResult } from '../../api/types';
import { RequirePermission } from '../../app/permissions';
import { useSession } from '../../app/session';
import { CopyField } from '../../components/copy-field';
import { ReasonDialog, ReasonField } from '../../components/reason-dialog';
import { useToast } from '../../components/toast';
import {
  Alert,
  Button,
  Card,
  Field,
  Input,
  JsonBlock,
  KeyValue,
  PageHeader,
  Select,
  Spinner,
  Textarea,
} from '../../components/ui';
import { formatDateTime, formatDuration, formatNumber, formatRelative } from '../../lib/format';
import { SERVERS_KEY, useServers } from './queries';
import { ListingBadge, OnlineBadge } from './server-status';

export function ServerDetailPage({ serverId }: { serverId: string }) {
  return (
    <RequirePermission permission="servers.read">
      <ServerDetail serverId={serverId} />
    </RequirePermission>
  );
}

function ServerDetail({ serverId }: { serverId: string }) {
  const { can } = useSession();
  const servers = useServers();
  const server = servers.data?.items.find((s) => s.id === serverId);

  const back = (
    <Link to="/servers" className="mb-3 inline-flex items-center gap-1 text-sm text-muted hover:text-ink">
      <ArrowLeft className="size-4" aria-hidden /> Servers
    </Link>
  );
  if (servers.isPending) return <Spinner />;
  if (servers.isError) return <Alert>{errorMessage(servers.error)}</Alert>;
  if (!server) {
    return (
      <>
        {back}
        <Alert>Server not found.</Alert>
      </>
    );
  }

  return (
    <>
      {back}
      <PageHeader
        title={server.name}
        description={
          <span className="inline-flex flex-wrap items-center gap-2">
            <OnlineBadge server={server} /> <ListingBadge listing={server.listing} />
            <span>
              {server.kind} · {server.region} · {server.mode.toUpperCase()}
            </span>
          </span>
        }
      />
      <div className="space-y-5">
        <LiveCard server={server} />
        {can('servers.command') && <CommandsCard server={server} />}
        {can('servers.manage') && <ManageCard server={server} />}
      </div>
    </>
  );
}

function LiveCard({ server }: { server: AdminServer }) {
  const live = server.live as Record<string, number | string> | null;
  return (
    <Card
      title="Live status"
      description={
        server.online
          ? `Updated ${formatRelative(String(live?.['updatedAt'] ?? ''))}`
          : `Last seen ${formatRelative(server.lastSeenAt)}`
      }
    >
      {live ? (
        <KeyValue
          items={[
            { label: 'Players', value: `${live['players']} / ${live['maxPlayers']}` },
            { label: 'Queue', value: live['queue'] },
            { label: 'Tick avg', value: `${formatNumber(Number(live['tickMsAvg']), 1)} ms` },
            { label: 'Tick p99', value: `${formatNumber(Number(live['tickMsP99']), 1)} ms` },
            { label: 'Server FPS', value: formatNumber(Number(live['fps'])) },
            { label: 'Memory', value: `${formatNumber(Number(live['memoryMb']))} MB` },
            { label: 'Entities', value: formatNumber(Number(live['entities'])) },
            { label: 'Uptime', value: formatDuration(Number(live['uptimeSeconds'])) },
            { label: 'Build', value: String(live['buildVersion']) },
            { label: 'Protocol', value: String(live['protocolVersion']) },
            { label: 'Host', value: server.host ?? '—' },
          ]}
        />
      ) : (
        <p className="text-sm text-muted">
          The server isn't connected. It appears here as soon as its link sends{' '}
          <code className="font-mono">hello</code>. Last seen: {formatDateTime(server.lastSeenAt)}.
        </p>
      )}
    </Card>
  );
}

type CommandName = 'broadcast' | 'kick' | 'save' | 'restart';

function CommandsCard({ server }: { server: AdminServer }) {
  const [name, setName] = useState<CommandName>('broadcast');
  const [message, setMessage] = useState('');
  const [accountId, setAccountId] = useState('');
  const [delay, setDelay] = useState(600);
  const [reason, setReason] = useState('');
  const [result, setResult] = useState<CommandResult | null>(null);
  const toast = useToast();

  const send = useMutation({
    mutationFn: () => {
      const args: Record<string, unknown> =
        name === 'broadcast'
          ? { message }
          : name === 'kick'
            ? { accountId, message }
            : name === 'restart'
              ? { delaySeconds: delay, message }
              : {};
      return call(() =>
        client.POST('/admin/v1/servers/{id}/commands', {
          params: { path: { id: server.id } },
          body: { name, args, reason: reason.trim() },
        }),
      );
    },
    onSuccess: (data) => {
      setResult(data);
      toast.show(
        data.ok ? `Server accepted "${name}"` : `Server rejected "${name}"`,
        data.ok ? 'ok' : 'danger',
      );
    },
  });

  return (
    <Card
      title="Send a command"
      description="Delivered over the server link; the server answers within 10 seconds."
    >
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          setResult(null);
          send.mutate();
        }}
      >
        <Field label="Command">
          {(p) => (
            <Select {...p} value={name} onChange={(e) => setName(e.target.value as CommandName)}>
              <option value="broadcast">Broadcast a message</option>
              <option value="kick">Kick a player</option>
              <option value="save">Save the world now</option>
              <option value="restart">Restart (with countdown)</option>
            </Select>
          )}
        </Field>
        {name === 'kick' && (
          <Field label="Player account id">
            {(p) => (
              <Input
                {...p}
                required
                pattern="[0-9a-fA-F-]{36}"
                value={accountId}
                onChange={(e) => setAccountId(e.target.value)}
                className="font-mono"
              />
            )}
          </Field>
        )}
        {name === 'restart' && (
          <Field label="Countdown (seconds)">
            {(p) => (
              <Input
                {...p}
                type="number"
                min={0}
                max={3600}
                value={delay}
                onChange={(e) => setDelay(Number(e.target.value))}
              />
            )}
          </Field>
        )}
        {name !== 'save' && (
          <Field label={name === 'broadcast' ? 'Message' : 'Message to players (optional)'}>
            {(p) => (
              <Textarea
                {...p}
                rows={2}
                maxLength={300}
                required={name === 'broadcast'}
                value={message}
                onChange={(e) => setMessage(e.target.value)}
              />
            )}
          </Field>
        )}
        <ReasonField value={reason} onChange={setReason} />
        {send.isError && <Alert>{errorMessage(send.error)}</Alert>}
        {result && (
          <div>
            <div className="mb-1 text-sm font-medium">Server response</div>
            <JsonBlock value={result} />
          </div>
        )}
        <Button
          type="submit"
          variant="primary"
          loading={send.isPending}
          disabled={!server.online}
          icon={<Send className="size-4" aria-hidden />}
        >
          Send
        </Button>
        {!server.online && (
          <p className="text-sm text-muted">The server is offline; commands need a live link.</p>
        )}
      </form>
    </Card>
  );
}

function ManageCard({ server }: { server: AdminServer }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [listing, setListing] = useState(server.listing);
  const [listingOpen, setListingOpen] = useState(false);
  const [rotateOpen, setRotateOpen] = useState(false);
  const [newToken, setNewToken] = useState<string | null>(null);

  return (
    <Card
      title="Management"
      description="Listing controls whether players can find and join the server. Every change is audited."
    >
      <div className="grid gap-6 md:grid-cols-2">
        <div className="space-y-3">
          <Field label="Listing" hint="Delisting disconnects the server immediately.">
            {(p) => (
              <Select {...p} value={listing} onChange={(e) => setListing(e.target.value)}>
                <option value="listed">Listed — visible in the browser</option>
                <option value="verified">Verified — listed with a badge</option>
                <option value="unlisted">Unlisted — join by id only</option>
                <option value="delisted">Delisted — blocked</option>
              </Select>
            )}
          </Field>
          <Button disabled={listing === server.listing} onClick={() => setListingOpen(true)}>
            Change listing
          </Button>
        </div>
        <div className="space-y-3">
          <div>
            <div className="text-sm font-medium">Server token</div>
            <p className="mt-1 text-sm text-muted">
              Rotate if the token leaked. The server disconnects until it's configured with the new token.
            </p>
          </div>
          <Button
            variant="danger"
            icon={<KeyRound className="size-4" aria-hidden />}
            onClick={() => setRotateOpen(true)}
          >
            Rotate token
          </Button>
          {newToken && (
            <div className="space-y-2">
              <Alert tone="warn">New token — shown only once.</Alert>
              <CopyField label="New server token" value={newToken} secret />
            </div>
          )}
        </div>
      </div>

      <ReasonDialog
        open={listingOpen}
        onClose={() => setListingOpen(false)}
        title={`Change listing to "${listing}"`}
        confirmLabel="Change listing"
        danger={listing === 'delisted'}
        onConfirm={async (reason) => {
          await call(() =>
            client.PATCH('/admin/v1/servers/{id}', {
              params: { path: { id: server.id } },
              body: { listing: listing as 'listed' | 'verified' | 'unlisted' | 'delisted', reason },
            }),
          );
          await queryClient.invalidateQueries({ queryKey: SERVERS_KEY });
          toast.show('Listing updated');
        }}
      />
      <ReasonDialog
        open={rotateOpen}
        onClose={() => setRotateOpen(false)}
        title="Rotate server token"
        description="The current token stops working immediately and the server is disconnected."
        confirmLabel="Rotate token"
        danger
        onConfirm={async (reason) => {
          const res = await call(() =>
            client.POST('/admin/v1/servers/{id}/token/rotate', {
              params: { path: { id: server.id } },
              body: { reason },
            }),
          );
          setNewToken(res.token);
          await queryClient.invalidateQueries({ queryKey: SERVERS_KEY });
        }}
      />
    </Card>
  );
}
