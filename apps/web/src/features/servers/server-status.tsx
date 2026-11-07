import type { AdminServer } from '../../api/types';
import { Badge } from '../../components/ui';

export function OnlineBadge({ server }: { server: AdminServer }) {
  return server.online ? (
    <Badge tone="ok">
      <span className="size-1.5 rounded-full bg-current" aria-hidden /> Online
    </Badge>
  ) : (
    <Badge>Offline</Badge>
  );
}

const listingTone = { listed: 'info', verified: 'ok', unlisted: 'neutral', delisted: 'danger' } as const;

export function ListingBadge({ listing }: { listing: string }) {
  return <Badge tone={listingTone[listing as keyof typeof listingTone] ?? 'neutral'}>{listing}</Badge>;
}

export function livePlayers(server: AdminServer): { players: number; max: number } {
  const live = server.live as { players?: number; maxPlayers?: number } | null;
  return { players: live?.players ?? 0, max: live?.maxPlayers ?? server.maxPlayers };
}
