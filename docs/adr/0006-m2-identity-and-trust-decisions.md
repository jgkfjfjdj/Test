# ADR 0006 — Identity, session and trust decisions made in M2

- **Status:** Accepted (2026-11-07)

| Decision | Why |
|----------|-----|
| Access tokens: Ed25519 JWT, 15 min, audience `kindling:player`. Refresh tokens: opaque, rotated on every use; reusing a rotated token revokes the whole session family | Theft detection; short blast radius. Consequence: clients must serialize refresh calls |
| Revocation of access tokens before expiry via Redis (`auth:revoked-sid:*`); **fails open** if Redis is down | Tokens live ≤ 15 min; taking the API down with Redis would be worse |
| Rate limits (Redis) **skip on Redis errors** | Same reasoning; per-email lockouts also degrade open |
| Sign in with Apple never auto-links to an existing email account; Apple email is used only if Apple marks it verified and no other account has it | Prevents account takeover via unverified emails. Linking flows come later |
| Uniform login errors + dummy Argon2 verification for unknown emails | No account enumeration via responses or timing (registration still reports "email taken", an accepted trade-off) |
| Email links open minimal server-rendered pages (strict CSP, no JS) | Verification and password reset work before any player website exists |
| Staff are a separate identity system: invitation only, password ≥ 14 chars + TOTP, cookie sessions (`HttpOnly`, `SameSite=Strict`, `Path=/admin`), CSRF token derived by HMAC from the session token, 8 h absolute / 30 min idle, 5-minute TOTP step-up for dangerous writes, single-use codes | Blueprint 09 §2; cookies keep tokens out of JavaScript |
| Audit log: written in the same transaction as the change; append-only enforced by a database trigger (UPDATE/DELETE raise for every role) | Tamper resistance that doesn't depend on deployment grants |
| Join tickets verified offline by servers **and** consumed once by the backend (`player_joined` → `player_joined_ack`) | Offline verification keeps joins fast; the backend check catches replays after server restarts and modified community servers |
| Commands to game servers are routed through Redis pub/sub to whichever API instance holds the link | Horizontal API scaling without sticky sessions |
| **App Attest deferred to M10** (was M2 in the roadmap) | Its value is as a risk signal; verifying attestations correctly needs real-device data we can't produce here. Doing it half-way would give false confidence |
