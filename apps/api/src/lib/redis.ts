import { Redis } from 'ioredis';

export function createRedis(url: string): Redis {
  return new Redis(url, {
    lazyConnect: true,
    // Fail fast instead of queueing commands while disconnected; callers decide how to degrade.
    enableOfflineQueue: false,
    maxRetriesPerRequest: 1,
    connectTimeout: 5_000,
    retryStrategy: (times) => Math.min(times * 250, 5_000),
  });
}
