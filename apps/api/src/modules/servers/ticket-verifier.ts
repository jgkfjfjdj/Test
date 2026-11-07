import { TICKET_ISSUER } from '@kindling/contracts';
import { createLocalJWKSet, jwtVerify, type JSONWebKeySet } from 'jose';

/**
 * Reference implementation of join-ticket verification, exactly as a game server must do it
 * (the C# version in the Unity server follows docs/protocol/join-tickets.md). Used by tests and
 * the fake game server.
 *
 * A ticket is accepted only if: the EdDSA signature matches a key from the backend JWKS,
 * iss = "kindling-tickets", aud = this server's id, it has not expired (±5 s), sub is an
 * account id, and its jti has not been seen before on this server.
 */
export interface VerifiedTicket {
  accountId: string;
  displayName: string;
  cosmetics: Record<string, string>;
  jti: string;
  expiresAt: number;
}

export class TicketRejected extends Error {
  constructor(public readonly reason: 'invalid' | 'expired' | 'wrong_server' | 'replayed') {
    super(`ticket rejected: ${reason}`);
    this.name = 'TicketRejected';
  }
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export class TicketVerifier {
  private keys: ReturnType<typeof createLocalJWKSet>;
  private readonly seen = new Map<string, number>();

  constructor(
    private readonly serverId: string,
    jwks: JSONWebKeySet,
    private readonly now: () => number = () => Date.now(),
  ) {
    this.keys = createLocalJWKSet(jwks);
  }

  /** Key rotation: replace the key set (backend pushes a new JWKS on welcome/rotation). */
  updateKeys(jwks: JSONWebKeySet): void {
    this.keys = createLocalJWKSet(jwks);
  }

  async verify(ticket: string): Promise<VerifiedTicket> {
    let payload;
    try {
      ({ payload } = await jwtVerify(ticket, this.keys, {
        issuer: TICKET_ISSUER,
        algorithms: ['EdDSA'],
        clockTolerance: 5,
        currentDate: new Date(this.now()),
      }));
    } catch (err) {
      const code = (err as { code?: string }).code;
      throw new TicketRejected(code === 'ERR_JWT_EXPIRED' ? 'expired' : 'invalid');
    }
    if (payload.aud !== this.serverId) throw new TicketRejected('wrong_server');
    if (
      typeof payload.sub !== 'string' ||
      !UUID.test(payload.sub) ||
      typeof payload.jti !== 'string' ||
      typeof payload.exp !== 'number'
    ) {
      throw new TicketRejected('invalid');
    }

    // Forget jtis whose tickets have expired; remember this one until it would expire.
    const nowSec = this.now() / 1000;
    for (const [jti, exp] of this.seen) if (exp + 5 < nowSec) this.seen.delete(jti);
    if (this.seen.has(payload.jti)) throw new TicketRejected('replayed');
    this.seen.set(payload.jti, payload.exp);

    return {
      accountId: payload.sub,
      displayName: typeof payload['dn'] === 'string' ? payload['dn'] : 'Survivor',
      cosmetics: (payload['cos'] as Record<string, string> | undefined) ?? {},
      jti: payload.jti,
      expiresAt: payload.exp,
    };
  }
}
