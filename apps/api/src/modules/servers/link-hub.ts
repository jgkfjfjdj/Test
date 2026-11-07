import { ErrorCodes, LinkCloseCodes, type CommandPayload, type LinkEnvelope } from '@kindling/contracts';
import { newId } from '@kindling/db';
import type { FastifyBaseLogger } from 'fastify';
import type { Redis } from 'ioredis';
import type { WebSocket } from 'ws';
import { ProblemError } from '../../lib/problem.js';

const controlChannel = (serverId: string) => `srvctl:${serverId}`;
const resultChannel = (commandId: string) => `srvcmd-result:${commandId}`;

type ControlMessage =
  | { kind: 'cmd'; id: string; command: CommandPayload }
  | { kind: 'disconnect'; code: number; reason: string; exceptConnectionId?: string };

interface Attached {
  socket: WebSocket;
  connectionId: string;
}

export interface CommandResult {
  ok: boolean;
  result?: unknown;
  error?: string;
}

/**
 * Tracks the server links connected to this API instance and routes commands to them.
 * Any instance can send a command: it is published on Redis (`srvctl:<serverId>`); the instance
 * holding that server's socket forwards it and publishes the reply on `srvcmd-result:<id>`.
 */
export class LinkHub {
  private readonly sockets = new Map<string, Attached>();
  private readonly waiters = new Map<string, (result: CommandResult) => void>();
  private subscriber?: Redis;
  private subscriberReady?: Promise<void>;

  constructor(
    private readonly redis: Redis,
    private readonly log: FastifyBaseLogger,
  ) {}

  private ensureSubscriber(): Promise<void> {
    if (!this.subscriberReady) {
      // Pub/sub needs its own connection; allow queueing until it is up.
      const sub = this.redis.duplicate({
        lazyConnect: true,
        enableOfflineQueue: true,
        maxRetriesPerRequest: null,
      });
      this.subscriber = sub;
      sub.on('message', (channel, message) => this.onMessage(channel, message));
      sub.on('error', (err) => this.log.debug({ err }, 'link hub subscriber error'));
      this.subscriberReady = sub.connect();
    }
    return this.subscriberReady;
  }

  private onMessage(channel: string, message: string): void {
    try {
      if (channel.startsWith('srvctl:')) {
        const serverId = channel.slice('srvctl:'.length);
        const attached = this.sockets.get(serverId);
        if (!attached) return;
        const control = JSON.parse(message) as ControlMessage;
        if (control.kind === 'cmd') {
          const envelope: LinkEnvelope<CommandPayload> = { t: 'cmd', id: control.id, d: control.command };
          attached.socket.send(JSON.stringify(envelope));
        } else if (control.kind === 'disconnect' && control.exceptConnectionId !== attached.connectionId) {
          attached.socket.close(control.code, control.reason);
        }
      } else if (channel.startsWith('srvcmd-result:')) {
        const id = channel.slice('srvcmd-result:'.length);
        this.waiters.get(id)?.(JSON.parse(message) as CommandResult);
      }
    } catch (err) {
      this.log.warn({ err, channel }, 'bad link hub message');
    }
  }

  /** Registers a connected server; an older connection for the same server is closed. */
  async attach(serverId: string, socket: WebSocket): Promise<void> {
    await this.ensureSubscriber();
    const connectionId = newId();
    const previous = this.sockets.get(serverId);
    this.sockets.set(serverId, { socket, connectionId });
    previous?.socket.close(LinkCloseCodes.replaced, 'replaced by a newer connection');
    await this.subscriber!.subscribe(controlChannel(serverId));
    // Other API instances holding an older link for this server drop it (this connection is exempt).
    await this.redis.publish(
      controlChannel(serverId),
      JSON.stringify({
        kind: 'disconnect',
        code: LinkCloseCodes.replaced,
        reason: 'replaced by a newer connection',
        exceptConnectionId: connectionId,
      } satisfies ControlMessage),
    );
  }

  async detach(serverId: string, socket: WebSocket): Promise<void> {
    if (this.sockets.get(serverId)?.socket !== socket) return; // already replaced
    this.sockets.delete(serverId);
    await this.subscriber?.unsubscribe(controlChannel(serverId)).catch(() => undefined);
  }

  isAttached(serverId: string, socket: WebSocket): boolean {
    return this.sockets.get(serverId)?.socket === socket;
  }

  /** Publishes a command result from the game server back to whoever is waiting for it. */
  async publishResult(commandId: string, result: CommandResult): Promise<void> {
    await this.redis.publish(resultChannel(commandId), JSON.stringify(result));
  }

  /** Sends a command to a server wherever its link is connected, and waits for the reply. */
  async sendCommand(serverId: string, command: CommandPayload, timeoutMs = 10_000): Promise<CommandResult> {
    await this.ensureSubscriber();
    const id = newId();
    const channel = resultChannel(id);
    const result = new Promise<CommandResult>((resolve) => this.waiters.set(id, resolve));
    await this.subscriber!.subscribe(channel);
    try {
      const receivers = await this.redis.publish(
        controlChannel(serverId),
        JSON.stringify({ kind: 'cmd', id, command } satisfies ControlMessage),
      );
      if (receivers === 0) {
        throw ProblemError.conflict(ErrorCodes.serverOffline, 'The server is not connected.');
      }
      let timer: NodeJS.Timeout | undefined;
      const timeout = new Promise<never>((_, reject) => {
        timer = setTimeout(
          () =>
            reject(
              new ProblemError(
                504,
                ErrorCodes.serverCommandTimeout,
                'Gateway Timeout',
                'The server did not answer in time.',
              ),
            ),
          timeoutMs,
        );
      });
      try {
        return await Promise.race([result, timeout]);
      } finally {
        clearTimeout(timer);
      }
    } finally {
      this.waiters.delete(id);
      await this.subscriber!.unsubscribe(channel).catch(() => undefined);
    }
  }

  /** Disconnects a server's link wherever it is connected (e.g. after token rotation). */
  async disconnect(serverId: string, code: number, reason: string): Promise<void> {
    await this.redis.publish(
      controlChannel(serverId),
      JSON.stringify({ kind: 'disconnect', code, reason } satisfies ControlMessage),
    );
  }

  async close(): Promise<void> {
    for (const { socket } of this.sockets.values()) socket.close(1001, 'backend shutting down');
    this.sockets.clear();
    if (this.subscriber) this.subscriber.disconnect();
  }
}
