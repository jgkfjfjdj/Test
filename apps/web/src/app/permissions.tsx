import { Lock } from 'lucide-react';
import type { ReactNode } from 'react';
import { useSession } from './session';

/** Renders children only if the signed-in staff member holds the permission. */
export function RequirePermission({ permission, children }: { permission: string; children: ReactNode }) {
  const { can } = useSession();
  if (can(permission)) return <>{children}</>;
  return (
    <div className="mx-auto max-w-md rounded-xl border border-line bg-surface p-6 text-center">
      <Lock className="mx-auto size-6 text-muted" aria-hidden />
      <h1 className="mt-2 text-lg font-semibold">No access</h1>
      <p className="mt-1 text-sm text-muted">
        This page needs the <code className="font-mono">{permission}</code> permission. Ask an administrator
        if you need it.
      </p>
    </div>
  );
}
