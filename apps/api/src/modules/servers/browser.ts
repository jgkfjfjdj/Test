import { ErrorCodes } from '@kindling/contracts';
import { accounts, gameServers, profiles } from '@kindling/db';
import { and, eq, ilike, inArray, ne, type SQL } from 'drizzle-orm';
import type { AppContext } from '../../context.js';
import { verifyPassword } from '../../lib/passwords.js';
import { ProblemError } from '../../lib/problem.js';
import { activeReservations, readLive, reserveSlot, ticketKey, type LiveState } from './live.js';
import type { GameServerRow } from './registry.js';

export interface BrowserFilters {
  region?: string | undefined;
  mode?: 'pvp' | 'pve' | undefined;
  official?: boolean | undefined;
  notFull?: boolean | undefined;
  hasPlayers?: boolean | undefined;
  noPassword?: boolean | undefined;
  search?: string | undefined;
  includeIncompatible?: boolean | undefined;
  cursor?: string | undefined;
  limit: number;
}

export interface ServerListItem {
  id: string;
  name: string;
  region: string;
  kind: string;
  verified: boolean;
  mode: string;
  tags: string[];
  mapId: string | null;
  players: number;
  maxPlayers: number;
  queue: number;
  passwordProtected: boolean;
  protocolVersion: number;
  compatible: boolean;
  host: string | null;
  gamePort: number | null;
  queryPort: number | null;
  lastWipeAt: string | null;
  nextWipeAt: string | null;
}

const MAX_SCAN = 2000;
const PASSWORD_ATTEMPTS = 10;

function toItem(row: GameServerRow, live: LiveState, clientProtocol: number | null): ServerListItem {
  return {
    id: row.id,
    name: row.name,
    region: row.region,
    kind: row.kind,
    verified: row.listing === 'verified',
    mode: row.mode,
    tags: row.tags,
    mapId: row.mapId,
    players: live.players,
    maxPlayers: live.maxPlayers,
    queue: live.queue,
    passwordProtected: row.passwordHash !== null,
    protocolVersion: live.protocolVersion,
    compatible: clientProtocol === null || clientProtocol === live.protocolVersion,
    host: row.host,
    gamePort: row.gamePort,
    queryPort: row.queryPort,
    lastWipeAt: row.lastWipeAt?.toISOString() ?? null,
    nextWipeAt: row.nextWipeAt?.toISOString() ?? null,
  };
}

/**
 * Server browser and join tickets. Static metadata comes from PostgreSQL; online status and
 * population from the Redis live index (maintained by server-link heartbeats).
 */
export class ServerBrowser {
  constructor(private readonly ctx: AppContext) {}

  async list(filters: BrowserFilters, clientProtocol: number | null) {
    const conditions: SQL[] = [inArray(gameServers.listing, ['listed', 'verified'])];
    if (filters.region) conditions.push(eq(gameServers.region, filters.region));
    if (filters.mode) conditions.push(eq(gameServers.mode, filters.mode));
    if (filters.official !== undefined)
      conditions.push(filters.official ? eq(gameServers.kind, 'official') : ne(gameServers.kind, 'official'));
    if (filters.search)
      conditions.push(ilike(gameServers.name, `%${filters.search.replace(/[%_\\]/g, '\\$&')}%`));

    const rows = await this.ctx.db
      .select()
      .from(gameServers)
      .where(and(...conditions))
      .limit(MAX_SCAN);
    const live = await readLive(
      this.ctx.redis,
      rows.map((r) => r.id),
    );

    let items = rows.flatMap((row) => {
      const state = live.get(row.id);
      return state ? [toItem(row, state, clientProtocol)] : [];
    });
    if (!filters.includeIncompatible) items = items.filter((i) => i.compatible);
    if (filters.notFull) items = items.filter((i) => i.players < i.maxPlayers);
    if (filters.hasPlayers) items = items.filter((i) => i.players > 0);
    if (filters.noPassword) items = items.filter((i) => !i.passwordProtected);

    // Official first, then busiest, then name; stable so cursor offsets stay meaningful.
    items.sort(
      (a, b) =>
        Number(b.kind === 'official') - Number(a.kind === 'official') ||
        b.players - a.players ||
        a.name.localeCompare(b.name) ||
        a.id.localeCompare(b.id),
    );
    const offset = filters.cursor ? Number(Buffer.from(filters.cursor, 'base64url').toString()) || 0 : 0;
    const page = items.slice(offset, offset + filters.limit);
    const next =
      offset + filters.limit < items.length
        ? Buffer.from(String(offset + filters.limit)).toString('base64url')
        : null;
    return { items: page, nextCursor: next, total: items.length };
  }

  async detail(serverId: string, clientProtocol: number | null) {
    const [row] = await this.ctx.db.select().from(gameServers).where(eq(gameServers.id, serverId)).limit(1);
    if (!row || row.listing === 'delisted')
      throw ProblemError.notFound('Server not found.', ErrorCodes.serverNotFound);
    const state = (await readLive(this.ctx.redis, [row.id])).get(row.id);
    return {
      id: row.id,
      name: row.name,
      description: row.description,
      region: row.region,
      kind: row.kind,
      verified: row.listing === 'verified',
      mode: row.mode,
      tags: row.tags,
      mapId: row.mapId,
      configSummary: row.configSummary,
      passwordProtected: row.passwordHash !== null,
      online: state !== undefined,
      live: state ? toItem(row, state, clientProtocol) : null,
    };
  }

  /**
   * Issues a join ticket: a short-lived Ed25519 JWT the game server verifies offline
   * (aud = server id, sub = account id, jti single-use). A slot is reserved for 60 seconds.
   */
  async join(
    accountId: string,
    serverId: string,
    input: { password?: string | undefined; protocol: number | null },
  ) {
    const [row] = await this.ctx.db.select().from(gameServers).where(eq(gameServers.id, serverId)).limit(1);
    if (!row || row.listing === 'delisted')
      throw ProblemError.notFound('Server not found.', ErrorCodes.serverNotFound);

    const live = (await readLive(this.ctx.redis, [serverId])).get(serverId);
    if (!live) throw ProblemError.conflict(ErrorCodes.serverOffline, 'This server is offline.');
    if (input.protocol !== null && input.protocol !== live.protocolVersion) {
      throw ProblemError.conflict(
        ErrorCodes.serverVersionMismatch,
        'This server runs a different game version.',
        {
          serverProtocol: live.protocolVersion,
        },
      );
    }

    const [account] = await this.ctx.db
      .select({ displayName: accounts.displayName, status: accounts.status, loadout: profiles.loadout })
      .from(accounts)
      .leftJoin(profiles, eq(profiles.accountId, accounts.id))
      .where(eq(accounts.id, accountId))
      .limit(1);
    if (!account) throw ProblemError.unauthorized();
    if (account.status !== 'active') {
      throw ProblemError.forbidden(
        ErrorCodes.accountSuspended,
        'This account cannot join servers right now.',
      );
    }

    if (row.passwordHash) {
      if (!input.password)
        throw ProblemError.forbidden(ErrorCodes.serverPasswordRequired, 'This server needs a password.');
      const attemptsKey = `srv:pw-attempts:${serverId}:${accountId}`;
      const attempts = Number((await this.ctx.redis.get(attemptsKey)) ?? 0);
      if (attempts >= PASSWORD_ATTEMPTS) {
        throw ProblemError.tooMany(
          ErrorCodes.tooManyAttempts,
          'Too many wrong passwords. Try again in 10 minutes.',
        );
      }
      if (!(await verifyPassword(row.passwordHash, input.password))) {
        await this.ctx.redis.multi().incr(attemptsKey).expire(attemptsKey, 600).exec();
        throw ProblemError.forbidden(ErrorCodes.serverPasswordWrong, 'Wrong server password.');
      }
    }

    const nowMs = this.ctx.now().getTime();
    const reserved = await reserveSlot(this.ctx.redis, serverId, accountId, nowMs);
    if (reserved === -1) throw ProblemError.conflict(ErrorCodes.serverOffline, 'This server is offline.');
    if (reserved === 0) throw ProblemError.conflict(ErrorCodes.serverFull, 'This server is full.');

    const ttl = this.ctx.config.JOIN_TICKET_TTL_SECONDS;
    const jti = crypto.randomUUID();
    const ticket = await this.ctx.keys.tickets.sign(
      { dn: account.displayName, cos: account.loadout ?? {} },
      {
        subject: accountId,
        audience: serverId,
        expiresInSeconds: ttl,
        issuedAt: Math.floor(nowMs / 1000),
        jwtId: jti,
      },
    );
    // Backend-side single use: consumed when the server reports player_joined.
    await this.ctx.redis.set(ticketKey(jti), `${accountId}:${serverId}`, 'EX', ttl + 60);

    return {
      ticket,
      expiresAt: new Date(nowMs + ttl * 1000).toISOString(),
      server: { id: serverId, host: row.host, gamePort: row.gamePort, protocolVersion: live.protocolVersion },
      reservation: {
        expiresInSeconds: 60,
        reservedSlots: await activeReservations(this.ctx.redis, serverId, nowMs),
      },
    };
  }
}
