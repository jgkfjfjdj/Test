import { Link, Outlet, useRouterState } from '@tanstack/react-router';
import { LogOut, Menu, X } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Button, Badge } from '../components/ui';
import { cn } from '../lib/cn';
import { NAV_GROUPS } from './nav';
import { useSession } from './session';

function Brand() {
  return (
    <div className="flex items-center gap-2.5">
      <img src="/admin/favicon.svg" alt="" className="size-8" />
      <div className="leading-tight">
        <div className="text-sm font-semibold tracking-wide">KINDLING</div>
        <div className="text-xs text-muted">Staff console</div>
      </div>
    </div>
  );
}

function NavLinks({ onNavigate }: { onNavigate?: () => void }) {
  const { can } = useSession();
  return (
    <nav aria-label="Main" className="space-y-5">
      {NAV_GROUPS.map((group, i) => {
        const items = group.items.filter((item) => !item.permission || can(item.permission));
        if (items.length === 0) return null;
        return (
          <div key={group.label ?? i}>
            {group.label && (
              <div className="mb-1.5 px-3 text-xs font-medium tracking-wide text-muted uppercase">
                {group.label}
              </div>
            )}
            <ul className="space-y-0.5">
              {items.map((item) => (
                <li key={item.to}>
                  <Link
                    to={item.to}
                    onClick={onNavigate}
                    activeOptions={{ exact: item.to === '/' }}
                    className="flex min-h-11 items-center gap-3 rounded-lg px-3 text-sm font-medium text-muted hover:bg-surface-2 hover:text-ink"
                    activeProps={{ className: '!bg-accent-soft !text-accent', 'aria-current': 'page' }}
                  >
                    <item.icon className="size-4 shrink-0" aria-hidden />
                    {item.label}
                  </Link>
                </li>
              ))}
            </ul>
          </div>
        );
      })}
    </nav>
  );
}

function UserCard() {
  const { staff, signOut } = useSession();
  const [busy, setBusy] = useState(false);
  if (!staff) return null;
  return (
    <div className="rounded-xl border border-line bg-surface-2 p-3">
      <div className="truncate text-sm font-medium">{staff.name}</div>
      <div className="truncate text-xs text-muted">{staff.email}</div>
      <div className="mt-2 flex flex-wrap gap-1">
        {staff.roles.map((role) => (
          <Badge key={role} className="border border-line bg-surface">
            {role.replace('_', ' ')}
          </Badge>
        ))}
      </div>
      <Button
        size="sm"
        variant="ghost"
        className="mt-2 w-full justify-start"
        loading={busy}
        icon={<LogOut className="size-4" aria-hidden />}
        onClick={async () => {
          setBusy(true);
          await signOut().finally(() => setBusy(false));
        }}
      >
        Sign out
      </Button>
    </div>
  );
}

/** App frame: fixed sidebar on large screens, top bar + slide-in menu on phones and tablets. */
export function Shell() {
  const [menuOpen, setMenuOpen] = useState(false);
  const drawer = useRef<HTMLDialogElement>(null);
  const pathname = useRouterState({ select: (s) => s.location.pathname });

  useEffect(() => {
    if (menuOpen && !drawer.current?.open) drawer.current?.showModal();
    if (!menuOpen && drawer.current?.open) drawer.current.close();
  }, [menuOpen]);
  useEffect(() => setMenuOpen(false), [pathname]);

  return (
    <div className="min-h-dvh lg:grid lg:grid-cols-[16rem_1fr]">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded focus:bg-surface focus:px-3 focus:py-2"
      >
        Skip to content
      </a>

      <aside className="sticky top-0 hidden h-dvh flex-col gap-6 overflow-y-auto border-r border-line bg-surface px-3 py-5 lg:flex">
        <div className="px-2">
          <Brand />
        </div>
        <div className="flex-1">
          <NavLinks />
        </div>
        <UserCard />
      </aside>

      <header className="sticky top-0 z-30 flex items-center justify-between border-b border-line bg-surface/95 px-4 py-2 pt-[max(0.5rem,env(safe-area-inset-top))] backdrop-blur lg:hidden">
        <Brand />
        <Button
          variant="ghost"
          aria-label="Open menu"
          aria-expanded={menuOpen}
          onClick={() => setMenuOpen(true)}
          className="px-3"
        >
          <Menu className="size-5" aria-hidden />
        </Button>
      </header>

      <dialog
        ref={drawer}
        aria-label="Menu"
        onClose={() => setMenuOpen(false)}
        className={cn(
          'm-0 ml-auto h-dvh max-h-none w-[min(20rem,85vw)] border-l border-line bg-surface p-0 text-ink lg:hidden',
        )}
      >
        <div className="flex h-full flex-col gap-6 overflow-y-auto px-3 py-4 pb-[max(1rem,env(safe-area-inset-bottom))]">
          <div className="flex items-center justify-between px-2">
            <Brand />
            <Button
              variant="ghost"
              aria-label="Close menu"
              onClick={() => setMenuOpen(false)}
              className="px-3"
            >
              <X className="size-5" aria-hidden />
            </Button>
          </div>
          <div className="flex-1">
            <NavLinks onNavigate={() => setMenuOpen(false)} />
          </div>
          <UserCard />
        </div>
      </dialog>

      <main
        id="main"
        className="mx-auto w-full max-w-6xl px-4 py-5 pb-[max(1.5rem,env(safe-area-inset-bottom))] sm:px-6 lg:px-8 lg:py-8"
      >
        <Outlet />
      </main>
    </div>
  );
}
