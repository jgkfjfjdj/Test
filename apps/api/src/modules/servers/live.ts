import type { HeartbeatPayload, HelloPayload } from '@kindling/contracts';
import type { Redis } from 'ioredis';

/** Live state expires unless refreshed by heartbeats (every 10 s), so crashed servers vanish from the browser. */
export const LIVE_TTL_SECONDS = 30;
export const RESERVATION_TTL_MS = 60_000;

export const liveKey = (serverId: string) => `srv:live:${serverId}`;
export const reserveKey = (serverId: string) => `srv:reserve:${serverId}`;
export const ticketKey = (jti: string) => `ticket:jti:${jti}`;

export interface LiveState {
  players: number;
  queue: number;
  maxPlayers: number;
  protocolVersion: number;
  buildVersion: string;
  tickMsAvg: number;
  tickMsP99: number;
  fps: number;
  memoryMb: number;
  entities: number;
  uptimeSeconds: number;
  updatedAt: string;
}

export async function writeHello(
  redis: Redis,
  serverId: string,
  hello: HelloPayload,
  now: Date,
): Promise<void> {
  await redis
    .multi()
    .del(liveKey(serverId))
    .hset(liveKey(serverId), {
      players: 0,
      queue: 0,
      maxPlayers: hello.maxPlayers,
      protocolVersion: hello.protocolVersion,
      buildVersion: hello.buildVersion,
      tickMsAvg: 0,
      tickMsP99: 0,
      fps: 0,
      memoryMb: 0,
      entities: 0,
      uptimeSeconds: 0,
      updatedAt: now.toISOString(),
    })
    .expire(liveKey(serverId), LIVE_TTL_SECONDS)
    .exec();
}

export async function writeHeartbeat(
  redis: Redis,
  serverId: string,
  hb: HeartbeatPayload,
  now: Date,
): Promise<boolean> {
  // Only refresh servers that said hello (the key holds max players and protocol from hello).
  const exists = await redis.exists(liveKey(serverId));
  if (!exists) return false;
  await redis
    .multi()
    .hset(liveKey(serverId), { ...hb, updatedAt: now.toISOString() })
    .expire(liveKey(serverId), LIVE_TTL_SECONDS)
    .exec();
  return true;
}

export async function clearLive(redis: Redis, serverId: string): Promise<void> {
  await redis.del(liveKey(serverId), reserveKey(serverId));
}

function parse(hash: Record<string, string>): LiveState | null {
  if (!hash['maxPlayers']) return null;
  const n = (k: string) => Number(hash[k] ?? 0);
  return {
    players: n('players'),
    queue: n('queue'),
    maxPlayers: n('maxPlayers'),
    protocolVersion: n('protocolVersion'),
    buildVersion: hash['buildVersion'] ?? '',
    tickMsAvg: n('tickMsAvg'),
    tickMsP99: n('tickMsP99'),
    fps: n('fps'),
    memoryMb: n('memoryMb'),
    entities: n('entities'),
    uptimeSeconds: n('uptimeSeconds'),
    updatedAt: hash['updatedAt'] ?? '',
  };
}

export async function readLive(redis: Redis, serverIds: string[]): Promise<Map<string, LiveState>> {
  const result = new Map<string, LiveState>();
  if (serverIds.length === 0) return result;
  const pipeline = redis.pipeline();
  for (const id of serverIds) pipeline.hgetall(liveKey(id));
  const replies = (await pipeline.exec()) ?? [];
  replies.forEach(([err, value], i) => {
    if (err) return;
    const state = parse(value as Record<string, string>);
    if (state) result.set(serverIds[i]!, state);
  });
  return result;
}

/**
 * Atomically reserves a slot: drops expired reservations, then admits the account if
 * players + reservations < maxPlayers (re-reserving refreshes an existing reservation).
 * Returns 1 = reserved, 0 = full, -1 = server offline.
 */
const RESERVE_SCRIPT = `
redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[1])
local players = redis.call('HGET', KEYS[2], 'players')
local maxPlayers = redis.call('HGET', KEYS[2], 'maxPlayers')
if not players or not maxPlayers then return -1 end
local existing = redis.call('ZSCORE', KEYS[1], ARGV[3])
local reserved = redis.call('ZCARD', KEYS[1])
if not existing and tonumber(players) + reserved >= tonumber(maxPlayers) then return 0 end
redis.call('ZADD', KEYS[1], ARGV[2], ARGV[3])
redis.call('PEXPIRE', KEYS[1], ARGV[4])
return 1
`;

export async function reserveSlot(
  redis: Redis,
  serverId: string,
  accountId: string,
  nowMs: number,
): Promise<-1 | 0 | 1> {
  const result = await redis.eval(
    RESERVE_SCRIPT,
    2,
    reserveKey(serverId),
    liveKey(serverId),
    String(nowMs),
    String(nowMs + RESERVATION_TTL_MS),
    accountId,
    String(RESERVATION_TTL_MS * 2),
  );
  return Number(result) as -1 | 0 | 1;
}

export async function activeReservations(redis: Redis, serverId: string, nowMs: number): Promise<number> {
  return redis.zcount(reserveKey(serverId), `(${nowMs}`, '+inf');
}
