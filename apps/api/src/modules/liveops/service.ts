import { ErrorCodes } from '@kindling/contracts';
import { maintenanceWindows, remoteConfig, versionPolicy, type MaintenanceScope } from '@kindling/db';
import { and, asc, gt, inArray, isNull, lte, or } from 'drizzle-orm';
import { createHash } from 'node:crypto';
import type { FastifyRequest } from 'fastify';
import type { AppContext } from '../../context.js';
import { ProblemError } from '../../lib/problem.js';

export type VersionStatus = 'ok' | 'update_available' | 'update_required';

export interface MaintenanceView {
  id: string;
  scope: MaintenanceScope;
  startsAt: string;
  endsAt: string | null;
  message: Record<string, string>;
}

export interface ClientInfo {
  platform: string;
  build: number | null;
  protocol: number | null;
  version: string | null;
}

/** Reads the client identification headers every app request carries. */
export function clientInfo(request: FastifyRequest): ClientInfo {
  const int = (name: string): number | null => {
    const raw = request.headers[name];
    if (typeof raw !== 'string' || !/^\d{1,9}$/.test(raw)) return null;
    return Number(raw);
  };
  const platform = request.headers['x-platform'];
  const version = request.headers['x-client-version'];
  return {
    platform: typeof platform === 'string' && /^[a-z]{2,16}$/.test(platform) ? platform : 'ios',
    build: int('x-build'),
    protocol: int('x-protocol'),
    version: typeof version === 'string' ? version.slice(0, 32) : null,
  };
}

const CACHE_MS = 5_000;
const UPCOMING_WINDOW_MS = 24 * 3600_000;

/** Which maintenance scopes block which features. A "full" window blocks everything. */
const BLOCKS: Record<'login' | 'matchmaking' | 'store', MaintenanceScope[]> = {
  login: ['full', 'login'],
  matchmaking: ['full', 'matchmaking'],
  store: ['full', 'store'],
};

/**
 * Version policy, maintenance windows and remote config. Reads are cached in-process for a few
 * seconds because every app start and sign-in consults them.
 */
export class LiveOpsService {
  private maintenanceCache?: { at: number; rows: MaintenanceView[] };
  private policyCache = new Map<string, { at: number; row: typeof versionPolicy.$inferSelect | null }>();

  constructor(private readonly ctx: AppContext) {}

  /** Drop caches (admin writes call this so changes apply on this instance immediately). */
  invalidate(): void {
    this.maintenanceCache = undefined;
    this.policyCache.clear();
  }

  async policy(platform: string) {
    const cached = this.policyCache.get(platform);
    if (cached && Date.now() - cached.at < CACHE_MS) return cached.row;
    const [row] = await this.ctx.db
      .select()
      .from(versionPolicy)
      .where(inArray(versionPolicy.platform, [platform]))
      .limit(1);
    this.policyCache.set(platform, { at: Date.now(), row: row ?? null });
    return row ?? null;
  }

  async versionStatus(client: ClientInfo) {
    const policy = await this.policy(client.platform);
    if (!policy)
      return { status: 'ok' as VersionStatus, latestBuild: null, minSupportedBuild: null, storeUrl: null };
    let status: VersionStatus = 'ok';
    if (client.build !== null && client.build < policy.minSupportedBuild) status = 'update_required';
    else if (client.build !== null && client.build < policy.latestBuild) status = 'update_available';
    return {
      status,
      latestBuild: policy.latestBuild,
      minSupportedBuild: policy.minSupportedBuild,
      storeUrl: policy.storeUrl,
    };
  }

  /** Windows that are active now or start within 24 hours (not cancelled, not ended). */
  async maintenance(): Promise<{ active: MaintenanceView[]; upcoming: MaintenanceView[] }> {
    const now = this.ctx.now();
    let rows =
      this.maintenanceCache && Date.now() - this.maintenanceCache.at < CACHE_MS
        ? this.maintenanceCache.rows
        : undefined;
    if (!rows) {
      const horizon = new Date(now.getTime() + UPCOMING_WINDOW_MS);
      const dbRows = await this.ctx.db
        .select()
        .from(maintenanceWindows)
        .where(
          and(
            isNull(maintenanceWindows.cancelledAt),
            lte(maintenanceWindows.startsAt, horizon),
            or(isNull(maintenanceWindows.endsAt), gt(maintenanceWindows.endsAt, now)),
          ),
        )
        .orderBy(asc(maintenanceWindows.startsAt));
      rows = dbRows.map((r) => ({
        id: r.id,
        scope: r.scope,
        startsAt: r.startsAt.toISOString(),
        endsAt: r.endsAt?.toISOString() ?? null,
        message: r.message,
      }));
      this.maintenanceCache = { at: Date.now(), rows };
    }
    const nowMs = now.getTime();
    const live = rows.filter((r) => r.endsAt === null || Date.parse(r.endsAt) > nowMs);
    return {
      active: live.filter((r) => Date.parse(r.startsAt) <= nowMs),
      upcoming: live.filter((r) => Date.parse(r.startsAt) > nowMs),
    };
  }

  /** Throws 503 platform.maintenance if a window blocking `feature` is active. */
  async assertAvailable(feature: keyof typeof BLOCKS): Promise<void> {
    const { active } = await this.maintenance();
    const blocking = active.find((w) => BLOCKS[feature].includes(w.scope));
    if (blocking) {
      throw ProblemError.unavailable(
        ErrorCodes.maintenance,
        blocking.message['en'] ?? 'Maintenance in progress.',
        {
          maintenance: blocking,
        },
      );
    }
  }

  /** Throws 426 platform.update_required when the client's build is below the supported minimum. */
  async assertSupportedBuild(client: ClientInfo): Promise<void> {
    const version = await this.versionStatus(client);
    if (version.status === 'update_required') {
      throw new ProblemError(
        426,
        ErrorCodes.updateRequired,
        'Upgrade Required',
        'Please update the game to continue.',
        undefined,
        {
          storeUrl: version.storeUrl,
        },
      );
    }
  }

  /** Client-visible remote config and a stable version tag (changes whenever any value changes). */
  async clientConfig(): Promise<{ version: string; values: Record<string, unknown> }> {
    const rows = await this.ctx.db
      .select({ key: remoteConfig.key, value: remoteConfig.value, version: remoteConfig.version })
      .from(remoteConfig)
      .where(inArray(remoteConfig.scope, ['client', 'both']))
      .orderBy(asc(remoteConfig.key));
    const values = Object.fromEntries(rows.map((r) => [r.key, r.value]));
    const version = createHash('sha256')
      .update(JSON.stringify(rows.map((r) => [r.key, r.version])))
      .digest('base64url')
      .slice(0, 16);
    return { version, values };
  }
}
