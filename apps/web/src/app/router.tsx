import {
  createRootRoute,
  createRoute,
  createRouter,
  Navigate,
  Outlet,
  useRouterState,
} from '@tanstack/react-router';
import { AuditPage, type AuditFilters } from '../features/audit/audit-page';
import { LoginPage } from '../features/auth/login-page';
import { SetupPage } from '../features/auth/setup-page';
import { ConfigPage } from '../features/liveops/config-page';
import { MaintenancePage } from '../features/liveops/maintenance-page';
import { VersionsPage } from '../features/liveops/versions-page';
import { OverviewPage } from '../features/overview/overview-page';
import { ServerDetailPage } from '../features/servers/server-detail-page';
import { ServersPage } from '../features/servers/servers-page';
import { StaffPage } from '../features/staff/staff-page';
import { Spinner } from '../components/ui';
import { useSession } from './session';
import { Shell } from './shell';

const str = (value: unknown): string | undefined =>
  typeof value === 'string' && value.length > 0 ? value.slice(0, 200) : undefined;

const rootRoute = createRootRoute({ component: Outlet });

const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/login',
  validateSearch: (search: Record<string, unknown>): { redirect?: string | undefined } => ({
    redirect: str(search['redirect']),
  }),
  component: function Login() {
    const { redirect } = loginRoute.useSearch();
    const { loading } = useSession();
    // Only allow same-app paths as a redirect target.
    return loading ? (
      <Spinner />
    ) : (
      <LoginPage redirect={redirect?.startsWith('/') && !redirect.startsWith('//') ? redirect : undefined} />
    );
  },
});

const setupRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/setup',
  validateSearch: (search: Record<string, unknown>): { token?: string | undefined } => ({
    token: str(search['token']),
  }),
  component: function Setup() {
    return <SetupPage token={setupRoute.useSearch().token} />;
  },
});

/** Everything below requires a staff session; otherwise redirect to login and come back after. */
const appRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: 'app',
  component: function Protected() {
    const { staff, loading } = useSession();
    // The location that is actually rendered (not a pending navigation), without the /admin basepath.
    const current = useRouterState({ select: (s) => s.resolvedLocation ?? s.location });
    if (loading) {
      return (
        <div className="flex min-h-dvh items-center justify-center">
          <Spinner label="Checking your session" />
        </div>
      );
    }
    if (!staff) {
      if (current.pathname.startsWith('/login')) return null; // navigation already under way
      const target = `${current.pathname}${current.searchStr ?? ''}`;
      return <Navigate to="/login" search={{ redirect: target === '/' ? undefined : target }} replace />;
    }
    return <Shell />;
  },
});

const overviewRoute = createRoute({ getParentRoute: () => appRoute, path: '/', component: OverviewPage });
const serversRoute = createRoute({
  getParentRoute: () => appRoute,
  path: '/servers',
  component: ServersPage,
});
const serverRoute = createRoute({
  getParentRoute: () => appRoute,
  path: '/servers/$serverId',
  component: function Server() {
    return <ServerDetailPage serverId={serverRoute.useParams().serverId} />;
  },
});
const maintenanceRoute = createRoute({
  getParentRoute: () => appRoute,
  path: '/maintenance',
  component: MaintenancePage,
});
const configRoute = createRoute({ getParentRoute: () => appRoute, path: '/config', component: ConfigPage });
const versionsRoute = createRoute({
  getParentRoute: () => appRoute,
  path: '/versions',
  component: VersionsPage,
});
const staffRoute = createRoute({ getParentRoute: () => appRoute, path: '/staff', component: StaffPage });
const auditRoute = createRoute({
  getParentRoute: () => appRoute,
  path: '/audit',
  validateSearch: (search: Record<string, unknown>): AuditFilters => ({
    action: str(search['action']),
    targetType: str(search['targetType']),
    targetId: str(search['targetId']),
    actorId: str(search['actorId']),
  }),
  component: function Audit() {
    return <AuditPage filters={auditRoute.useSearch()} />;
  },
});

const routeTree = rootRoute.addChildren([
  loginRoute,
  setupRoute,
  appRoute.addChildren([
    overviewRoute,
    serversRoute,
    serverRoute,
    maintenanceRoute,
    configRoute,
    versionsRoute,
    staffRoute,
    auditRoute,
  ]),
]);

export const router = createRouter({
  routeTree,
  basepath: '/admin',
  defaultPreload: 'intent',
  defaultNotFoundComponent: () => (
    <div className="p-8 text-center">
      <h1 className="text-lg font-semibold">Page not found</h1>
    </div>
  ),
});

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
