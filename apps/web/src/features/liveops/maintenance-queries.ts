import { useQuery } from '@tanstack/react-query';
import { call, client } from '../../api/client';
import type { MaintenanceWindow } from '../../api/types';

export const MAINTENANCE_KEY = ['maintenance'] as const;

export function useMaintenance(enabled = true) {
  return useQuery({
    queryKey: MAINTENANCE_KEY,
    queryFn: () => call(() => client.GET('/admin/v1/maintenance')),
    refetchInterval: 30_000,
    enabled,
  });
}

export type WindowState = 'active' | 'scheduled' | 'ended' | 'cancelled';

export function windowState(w: MaintenanceWindow, now = Date.now()): WindowState {
  if (w.cancelledAt) return 'cancelled';
  if (w.endsAt && Date.parse(w.endsAt) <= now) return 'ended';
  return Date.parse(w.startsAt) <= now ? 'active' : 'scheduled';
}
