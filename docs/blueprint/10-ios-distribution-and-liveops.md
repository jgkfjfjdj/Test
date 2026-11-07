# 10 — iOS Distribution, Updates and Live Ops

We follow Apple's distribution model completely: **the App Store for releases, TestFlight for betas,
and downloadable content limited to data and assets (never executable code).** Nothing in this
project tries to get around iOS security or distribution rules.

## 1. Channels

| Channel | Audience | Notes |
|---------|----------|-------|
| Xcode / development builds | You (and me through CI artifacts) | Daily testing on device |
| **TestFlight – internal** | Up to 100 team members | No review needed; points to `dev` or `staging` |
| **TestFlight – external** | Up to 10 000 testers | First build of each version goes through Beta App Review; builds expire after 90 days; points to `production` with a beta flag (beta-only servers) |
| **App Store** | Public | Full App Review; phased release (7-day rollout) enabled |

## 2. Build and release pipeline

```
git tag client/v1.2.0 ──► CI (macOS runner or your Mac)
   1. Unity batchmode build → Xcode project (IL2CPP)
   2. Fastlane:
        match  → signing certificates and profiles (encrypted git or cloud storage)
        gym    → .ipa archive
        pilot  → upload to TestFlight (+ changelog)
   3. Upload dSYMs to Sentry
   4. Register version in backend (status = testing)  ← admin console or CI API call
Promote: TestFlight internal → external → submit for App Review → phased release
After release: set status = live in the console; later raise min_supported_build when needed
```

Signing secrets (App Store Connect API key, match password) are stored as CI secrets, never in the repo.

## 3. Versioning scheme

| Version | Format | Owner | Example |
|---------|--------|-------|---------|
| Marketing version (`CFBundleShortVersionString`) | `MAJOR.MINOR.PATCH` | Release manager | `1.3.0` |
| Build number (`CFBundleVersion`) | Increases every build | CI | `1300042` |
| Protocol version | Integer | `Kindling.Net` | `17` |
| Content version | `YYYY.MM.N` | Content pipeline | `2028.12.1` |
| Server build | `MAJOR.MINOR.PATCH+build` | CI | `1.3.0+812` |

**Compatibility rules**
- Client ↔ game server: the **protocol version must match**.
- Client ↔ backend: REST is backwards compatible within `/v1`; the backend supports every build at or
  above `min_supported_build`.
- Client ↔ content: each content release states the build range it supports (`min_build..max_build`).
  The bootstrap only returns catalogs compatible with the calling build.

## 4. Version verification flow
1. The client sends its version headers to `/v1/bootstrap`.
2. The backend compares the build with `version_policy`:
   - `build < min_supported_build` → **`update_required`**: a blocking screen with an App Store button.
   - `build < latest_build` → **`update_available`**: a dismissible prompt (at most once a day).
   - Build status `blocked` (e.g. a critical bug) → treated as `update_required`.
3. The game server also checks the protocol at handshake (`version_mismatch` → the client shows the
   update screen).
4. **Rolling out a protocol bump:** release the new client (phased) → publish the matching server build
   (mandatory flag) → official servers update in a scheduled window → raise `min_supported_build` once
   most players have updated (watched in analytics) → community servers update automatically
   (supervisor) or get flagged "outdated" in the browser.

## 5. Downloadable content (DLC) on iOS

| What | Delivered how | Allowed? |
|------|---------------|----------|
| Core game, launch map, base items | **Inside the app binary** | ✓ |
| Seasonal assets (snow textures, winter props, cosmetics' meshes and materials, decorations) | **Addressables remote bundles** on the CDN | ✓ (data/assets only) |
| Item/recipe/loot definitions for seasons | ScriptableObject data **for C# behaviors already in the binary** | ✓ |
| New gameplay *code* | **Only through an App Store update** | Downloading code is not allowed (App Review Guideline 2.5.2) |

**Addressables setup**
- Groups: `Core` (local), `Map_<id>` (local at launch), `Season_<key>` (remote), `Cosmetics_<set>` (remote).
- The remote catalog URL comes from `/v1/bootstrap` (a `content_releases` row compatible with the build),
  so we can stage and roll back content releases without a binary update.
- Integrity: catalog served over HTTPS with its hash checked against the backend value; bundle CRC
  checks turned on.
- UX: download size shown first; asks before large downloads on cellular (> 50 MB); can download in the
  background while the player is in the menus; resumes after interruption; storage-full handling.
- **Seasonal C# behavior** (e.g. the snowfall gameplay system) ships in the binary *before* the season,
  stays turned off, and is activated by date and remote config. That's why seasonal planning starts
  with a binary update deadline (see [11](11-seasonal-content-and-winter-update.md)).

App size target: base install ≤ 600 MB (monitored per build). If needed, Apple's Background Assets
framework can deliver large asset packs before first launch (evaluated in M10).

## 6. Backend-controlled notices
- **Maintenance:** scheduled or immediate, per scope (full / login / matchmaking / store), localized
  message; banner 24 h ahead; blocking screen while active (details in [07 §14](07-backend.md#14-maintenance)).
- **Announcements:** main-menu cards, in-game toasts, server broadcasts, optional push; targeting and
  scheduling.
- **Remote config / feature flags:** see [07 §13](07-backend.md#13-remote-config-and-feature-flags).
  Clients always ship with **safe defaults** built in, so the game works if config can't be fetched.

## 7. In-app purchases (post-launch, cosmetics only)
- StoreKit 2 products (non-consumable cosmetics, plus optional seasonal bundles). **No paid random
  loot boxes**; if any randomized item is ever sold, the odds must be shown, as Apple requires.
- Flow: purchase on device → the client sends the **JWS signed transaction** to
  `/v1/store/apple/transactions` → the backend verifies it (App Store Server Library) → grants the
  entitlement → the client refreshes. **App Store Server Notifications V2** handle refunds and
  revocations (the entitlement is removed and the event audited). A *Restore Purchases* button is
  provided.
- Gameplay advantages are never sold. Community servers may not sell advantages either (see
  [06 §7.3](06-dedicated-servers.md#73-registration-and-listing)).

## 8. App Store compliance checklist (checked before every submission)

| Requirement | How we meet it |
|-------------|----------------|
| Account deletion inside the app | Settings → Account → Delete account (`DELETE /v1/me`) |
| Sign in with Apple when offering third-party logins | Offered from day one |
| User-generated content rules (chat, names, signs) | Filtering, reporting, blocking, published contact info, moderation team/process |
| Privacy policy + App Privacy "nutrition label" | Written in M10; matches what analytics and Sentry actually collect |
| IPv6-only network support | Dual-stack networking; NAT64 test before submission |
| No downloaded executable code | Addressables carry data and assets only |
| Age rating questionnaire | Fantasy/realistic violence, unrestricted web access = no, UGC chat = yes. Expected rating **17+** (confirmed in M10) |
| Microphone permission text | Only if voice ships; push-to-talk; off by default |
| Push permission | Asked in context (after the first base is built), not at launch |
| Export compliance (encryption) | HTTPS/TLS only → declared in `ITSAppUsesNonExemptEncryption` per Apple's guidance |
| In-app purchases via Apple only, no external purchase links | Enforced in UI and in community server rules |
| Loot box odds disclosure | Not applicable (none sold); rule kept in content policy |

## 9. Live ops calendar (operating model)
- **Weekly:** server-side balance tweaks (remote config / content data for servers), events.
- **Every 2–4 weeks:** client patch (bug fixes, small content).
- **Every 3 months:** season (Season Pack + binary update ~3 weeks before the season starts).
- Every change has an owner, a schedule in the console, and a rollback plan (config history, content
  rollback, build withdrawal).
