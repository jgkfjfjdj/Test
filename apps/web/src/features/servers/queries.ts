import { useQuery } from '@tanstack/react-query';
import { call, client } from '../../api/client';

export const SERVERS_KEY = ['servers'] as const;

/** Live server list; refreshed every 10 s (heartbeat cadence). */
export function useServers(enabled = true) {
  return useQuery({
    queryKey: SERVERS_KEY,
    queryFn: () => call(() => client.GET('/admin/v1/servers')),
    refetchInterval: 10_000,
    enabled,
  });
}
