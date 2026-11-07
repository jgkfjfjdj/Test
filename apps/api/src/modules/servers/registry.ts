import { ErrorCodes } from '@kindling/contracts';
import { gameServers, newId, serverTokens, type ServerListing, type ServerMode } from '@kindling/db';
import { and, eq, isNull } from 'drizzle-orm';
import type { AppContext } from '../../context.js';
import { randomToken, sha256 } from '../../lib/crypto.js';
import { ProblemError } from '../../lib/problem.js';

/** Server tokens are prefixed so secret scanners and humans can recognise them. */
export const SERVER_TOKEN_PREFIX = 'kst_';

export type GameServerRow = typeof gameServers.$inferSelect;

export class ServerRegistry {
  constructor(private readonly ctx: AppContext) {}

  private newToken(): string {
    return `${SERVER_TOKEN_PREFIX}${randomToken()}`;
  }

  async createOfficial(input: {
    name: string;
    region: string;
    maxPlayers: number;
    mode: ServerMode;
    tags: string[];
    listing: ServerListing;
  }): Promise<{ server: GameServerRow; token: string }> {
    const id = newId();
    const token = this.newToken();
    const server = await this.ctx.db.transaction(async (tx) => {
      const [row] = await tx
        .insert(gameServers)
        .values({
          id,
          kind: 'official',
          name: input.name,
          region: input.region,
          maxPlayers: input.maxPlayers,
          mode: input.mode,
          tags: input.tags,
          listing: input.listing,
        })
        .returning();
      await tx.insert(serverTokens).values({ id: newId(), serverId: id, tokenHash: sha256(token) });
      return row!;
    });
    return { server, token };
  }

  /** Revokes every token of the server and issues a new one (shown once). */
  async rotateToken(serverId: string): Promise<string> {
    const token = this.newToken();
    await this.ctx.db.transaction(async (tx) => {
      const [server] = await tx
        .select({ id: gameServers.id })
        .from(gameServers)
        .where(eq(gameServers.id, serverId))
        .limit(1);
      if (!server) throw ProblemError.notFound('Server not found.', ErrorCodes.serverNotFound);
      await tx
        .update(serverTokens)
        .set({ revokedAt: this.ctx.now() })
        .where(and(eq(serverTokens.serverId, serverId), isNull(serverTokens.revokedAt)));
      await tx.insert(serverTokens).values({ id: newId(), serverId, tokenHash: sha256(token) });
    });
    return token;
  }

  async authenticate(token: string): Promise<GameServerRow | null> {
    if (!token.startsWith(SERVER_TOKEN_PREFIX) || token.length > 100) return null;
    const [row] = await this.ctx.db
      .select({ server: gameServers })
      .from(serverTokens)
      .innerJoin(gameServers, eq(gameServers.id, serverTokens.serverId))
      .where(and(eq(serverTokens.tokenHash, sha256(token)), isNull(serverTokens.revokedAt)))
      .limit(1);
    return row?.server ?? null;
  }

  async get(serverId: string): Promise<GameServerRow | null> {
    const [row] = await this.ctx.db.select().from(gameServers).where(eq(gameServers.id, serverId)).limit(1);
    return row ?? null;
  }
}
