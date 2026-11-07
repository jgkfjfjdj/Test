# Join tickets — specification for game servers

A **join ticket** is how a game server knows that a connecting player is a real, signed-in,
non-banned account that was allowed to join *this* server. Tickets are verified **offline** by the
game server (no backend call needed), then confirmed with the backend over the server link.

Reference implementation (TypeScript): `apps/api/src/modules/servers/ticket-verifier.ts`.
The Unity dedicated server must implement exactly the same checks in C# (M2 Unity part).

## Format

A compact JWS (JWT) signed with **EdDSA / Ed25519**.

Header: `{"alg":"EdDSA","kid":"<key id>","typ":"JWT"}`

| Claim | Meaning |
|-------|---------|
| `iss` | Always `kindling-tickets` |
| `aud` | The server id (UUID) the ticket is for |
| `sub` | Account id (UUID) |
| `jti` | Unique ticket id (single use) |
| `iat`, `exp` | Issued-at and expiry (Unix seconds); lifetime 60 s by default |
| `dn` | Display name |
| `cos` | Cosmetic loadout: `{ slot: itemKey }` |

## Keys

- On `welcome` (server link handshake) the backend sends `jwks` (`{ "keys": [JWK…] }`).
  The same document is served at `GET /server/v1/jwks` (cache 5 minutes).
- Each key: `{"kty":"OKP","crv":"Ed25519","x":"<base64url public key>","kid":"…","alg":"EdDSA"}`.
- Select the key by the header `kid`. Unknown `kid` → reject. Keep all keys from the JWKS
  (several exist during rotation).

## Verification steps (all must pass)

1. Split into `header.payload.signature`; base64url-decode header and payload (reject on errors).
2. `alg` must be `EdDSA`; find the JWK with the header's `kid`.
3. Verify the Ed25519 signature over the ASCII bytes of `header.payload`.
4. `iss == "kindling-tickets"`.
5. `aud == <this server's id>` (from `welcome.d.serverId`) — otherwise `wrong_server`.
6. `exp > now - 5 s` (5-second clock tolerance) — otherwise `expired`.
7. `sub` is a UUID and `jti` is a non-empty string.
8. `jti` has **not** been accepted before on this server. Remember accepted `jti`s until their
   `exp` (+5 s) — otherwise `replayed`.
9. Send `player_joined {accountId: sub, jti}` on the server link and wait for
   `player_joined_ack`. If `ok` is false, disconnect the player (the backend never issued the
   ticket, or it was already used — e.g. replay after a server restart cleared the local cache).

Rejected tickets → disconnect with a reason code; never let the player spawn first.

## C# notes (Unity, IL2CPP)

- .NET Standard 2.1 has no built-in Ed25519. Use a small, pure-C# implementation that is
  IL2CPP-safe (e.g. a vetted port of TweetNaCl / Chaos.NaCl verification), vendored with its license.
  Test it against the vectors produced by this backend (CI can export sample tickets).
- Do not use reflection-based JSON for the hot path; the payload is tiny — a minimal parser is fine.
