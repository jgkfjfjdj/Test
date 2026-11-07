import { createHash } from 'node:crypto';
import { createRemoteJWKSet, jwtVerify, type JWTVerifyGetKey } from 'jose';

export const APPLE_ISSUER = 'https://appleid.apple.com';
export const APPLE_JWKS_URL = 'https://appleid.apple.com/auth/keys';

export interface AppleIdentity {
  /** Stable user id ("sub"), unique per Apple developer team. */
  subject: string;
  email: string | null;
  emailVerified: boolean;
  isPrivateRelayEmail: boolean;
}

export interface AppleTokenVerifier {
  /**
   * Verifies a Sign in with Apple identity token. `rawNonce` is the nonce the app generated;
   * the app sends sha256(rawNonce) to Apple, which places it in the token's "nonce" claim.
   */
  verify(identityToken: string, rawNonce: string): Promise<AppleIdentity>;
}

export class AppleTokenError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'AppleTokenError';
  }
}

function claimIsTrue(value: unknown): boolean {
  return value === true || value === 'true';
}

/** Verifies identity tokens against a JWKS (Apple's in production, a local one in tests). */
export class JwksAppleTokenVerifier implements AppleTokenVerifier {
  constructor(
    private readonly clientIds: string[],
    private readonly keys: JWTVerifyGetKey,
  ) {
    if (clientIds.length === 0) throw new Error('At least one Apple client id is required');
  }

  static forApple(clientIds: string[]): JwksAppleTokenVerifier {
    return new JwksAppleTokenVerifier(
      clientIds,
      createRemoteJWKSet(new URL(APPLE_JWKS_URL), { cacheMaxAge: 6 * 3600_000, timeoutDuration: 5000 }),
    );
  }

  async verify(identityToken: string, rawNonce: string): Promise<AppleIdentity> {
    let payload;
    try {
      ({ payload } = await jwtVerify(identityToken, this.keys, {
        issuer: APPLE_ISSUER,
        audience: this.clientIds,
        algorithms: ['RS256', 'ES256'],
        clockTolerance: 10,
      }));
    } catch (err) {
      throw new AppleTokenError(
        `identity token rejected: ${err instanceof Error ? err.message : String(err)}`,
      );
    }
    const expectedNonce = createHash('sha256').update(rawNonce).digest('hex');
    if (payload['nonce'] !== expectedNonce) throw new AppleTokenError('nonce mismatch');
    if (typeof payload.sub !== 'string' || payload.sub.length === 0)
      throw new AppleTokenError('missing subject');
    const email = typeof payload['email'] === 'string' ? payload['email'] : null;
    return {
      subject: payload.sub,
      email,
      emailVerified: email !== null && claimIsTrue(payload['email_verified']),
      isPrivateRelayEmail: claimIsTrue(payload['is_private_email']),
    };
  }
}
