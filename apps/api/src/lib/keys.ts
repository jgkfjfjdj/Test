import { exportJWK, generateKeyPair, importJWK, jwtVerify, SignJWT, type JWK, type JWTPayload } from 'jose';
import { randomToken } from './crypto.js';

type SigningKey = Parameters<SignJWT['sign']>[0];

interface LoadedKey {
  kid: string;
  privateKey: SigningKey;
  publicKey: SigningKey;
  publicJwk: JWK;
}

export interface SignOptions {
  subject?: string;
  audience?: string;
  expiresInSeconds: number;
  /** Override "now" (seconds); tests use it to mint already-expired tokens. */
  issuedAt?: number;
  jwtId?: string;
}

export interface VerifyOptions {
  audience?: string;
  /** Override "now" for expiry checks (Date). */
  currentDate?: Date;
}

/**
 * Ed25519 (EdDSA) signing keys for one purpose (access tokens or join tickets).
 * The first key signs; all keys verify, so rotation works by prepending a new key and later
 * dropping the old one after its tokens have expired.
 */
export class KeyRing {
  private constructor(
    private readonly issuer: string,
    private readonly keys: LoadedKey[],
  ) {}

  static async fromJwks(issuer: string, privateJwks: JWK[]): Promise<KeyRing> {
    if (privateJwks.length === 0) throw new Error(`KeyRing(${issuer}): at least one key is required`);
    const keys: LoadedKey[] = [];
    for (const jwk of privateJwks) {
      if (jwk.kty !== 'OKP' || jwk.crv !== 'Ed25519' || !jwk.d || !jwk.kid) {
        throw new Error(`KeyRing(${issuer}): keys must be private Ed25519 JWKs with a "kid"`);
      }
      const { d: _d, ...publicJwk } = jwk;
      keys.push({
        kid: jwk.kid,
        privateKey: await importJWK({ ...jwk, alg: 'EdDSA' }, 'EdDSA'),
        publicKey: await importJWK({ ...publicJwk, alg: 'EdDSA' }, 'EdDSA'),
        publicJwk: { ...publicJwk, alg: 'EdDSA', use: 'sig' },
      });
    }
    return new KeyRing(issuer, keys);
  }

  /** Parses an env value (JSON array of private JWKs) or generates an ephemeral key when absent. */
  static async fromEnv(
    issuer: string,
    value: string | undefined,
  ): Promise<{ ring: KeyRing; ephemeral: boolean }> {
    if (value) {
      const parsed = JSON.parse(value) as JWK | JWK[];
      return {
        ring: await KeyRing.fromJwks(issuer, Array.isArray(parsed) ? parsed : [parsed]),
        ephemeral: false,
      };
    }
    return { ring: await KeyRing.fromJwks(issuer, [await generatePrivateJwk()]), ephemeral: true };
  }

  get activeKid(): string {
    return this.keys[0]!.kid;
  }

  async sign(claims: JWTPayload & Record<string, unknown>, options: SignOptions): Promise<string> {
    const active = this.keys[0]!;
    const iat = options.issuedAt ?? Math.floor(Date.now() / 1000);
    let jwt = new SignJWT(claims)
      .setProtectedHeader({ alg: 'EdDSA', kid: active.kid, typ: 'JWT' })
      .setIssuer(this.issuer)
      .setIssuedAt(iat)
      .setExpirationTime(iat + options.expiresInSeconds);
    if (options.subject) jwt = jwt.setSubject(options.subject);
    if (options.audience) jwt = jwt.setAudience(options.audience);
    if (options.jwtId) jwt = jwt.setJti(options.jwtId);
    return jwt.sign(active.privateKey);
  }

  /** Verifies signature, issuer, expiry and (optionally) audience. Throws on any failure. */
  async verify(token: string, options: VerifyOptions = {}): Promise<JWTPayload> {
    const { payload } = await jwtVerify(
      token,
      async (header) => {
        const key = this.keys.find((k) => k.kid === header.kid);
        if (!key) throw new Error('unknown key id');
        return key.publicKey;
      },
      {
        issuer: this.issuer,
        algorithms: ['EdDSA'],
        ...(options.audience ? { audience: options.audience } : {}),
        ...(options.currentDate ? { currentDate: options.currentDate } : {}),
        clockTolerance: 5,
      },
    );
    return payload;
  }

  /** Public JWKS document (served to game servers for ticket verification). */
  publicJwks(): { keys: JWK[] } {
    return { keys: this.keys.map((k) => k.publicJwk) };
  }
}

export async function generatePrivateJwk(kid = `k_${randomToken(9)}`): Promise<JWK> {
  const { privateKey } = await generateKeyPair('EdDSA', { crv: 'Ed25519', extractable: true });
  const jwk = await exportJWK(privateKey);
  return { ...jwk, kid };
}
