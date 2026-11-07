import { useQuery, useQueryClient } from '@tanstack/react-query';
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { ApiError, call, client, setCsrfToken, setUnauthorizedHandler } from '../api/client';
import type { StaffMe } from '../api/types';

interface SessionValue {
  staff: StaffMe | null;
  loading: boolean;
  /** True after the server ended the session (expired, revoked) so the login page can explain. */
  sessionEnded: boolean;
  can: (permission: string) => boolean;
  signedIn: (staff: StaffMe) => void;
  signOut: () => Promise<void>;
}

const SessionContext = createContext<SessionValue | null>(null);
export const ME_KEY = ['me'] as const;

export function SessionProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [sessionEnded, setSessionEnded] = useState(false);

  const me = useQuery({
    queryKey: ME_KEY,
    queryFn: async (): Promise<StaffMe | null> => {
      try {
        const staff = await call(() => client.GET('/admin/v1/auth/me'));
        setCsrfToken(staff.csrfToken);
        return staff;
      } catch (err) {
        if (err instanceof ApiError && err.status === 401) return null;
        throw err;
      }
    },
    staleTime: 60_000,
    retry: false,
  });

  useEffect(() => {
    setUnauthorizedHandler(() => {
      setCsrfToken(null);
      setSessionEnded(true);
      queryClient.setQueryData(ME_KEY, null);
    });
    return () => setUnauthorizedHandler(null);
  }, [queryClient]);

  const signedIn = useCallback(
    (staff: StaffMe) => {
      setCsrfToken(staff.csrfToken);
      setSessionEnded(false);
      queryClient.setQueryData(ME_KEY, staff);
    },
    [queryClient],
  );

  const signOut = useCallback(async () => {
    try {
      await call(() => client.POST('/admin/v1/auth/logout'));
    } finally {
      setCsrfToken(null);
      queryClient.clear();
      queryClient.setQueryData(ME_KEY, null);
    }
  }, [queryClient]);

  const staff = me.data ?? null;
  const value = useMemo<SessionValue>(
    () => ({
      staff,
      loading: me.isPending,
      sessionEnded,
      can: (permission) => staff?.permissions.includes(permission) ?? false,
      signedIn,
      signOut,
    }),
    [staff, me.isPending, sessionEnded, signedIn, signOut],
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): SessionValue {
  const ctx = useContext(SessionContext);
  if (!ctx) throw new Error('useSession outside SessionProvider');
  return ctx;
}
