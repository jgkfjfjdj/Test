# 13 — Software, Hardware and Accounts You Need

Versions: install the **latest stable release of each line listed**. Exact versions get pinned in the
repository in M0, and that pinned version is the one to use.

## 1. Hardware

| Item | Required? | Notes |
|------|-----------|-------|
| **Mac with Apple Silicon** (M1 or newer, 16 GB RAM+, 512 GB+ SSD) | **Required for iOS builds** (or use cloud macOS CI; still strongly recommended) | Runs the Unity Editor, Xcode, Instruments. Unity projects + Library folders are large |
| **iPhone for testing: an older one** (iPhone 11 / SE 2nd gen / 12) | Required | Our Low tier: performance truth |
| **iPhone for testing: a recent one** (iPhone 15 or newer) | Strongly recommended | High tier, ProMotion behavior |
| A game controller (Xbox or DualSense) | Optional | Controller support testing |
| A Windows or Linux PC | Optional | You can run Unity and the backend on it, but it **can't build iOS** |

## 2. Accounts and services

| Account | Cost (approximate, check current prices) | When | Purpose |
|---------|-------------------------------------------|------|---------|
| **Apple Developer Program** | US$99/year | Before M1 device testing | Device builds, TestFlight, App Store, Sign in with Apple, push, App Attest |
| **Unity ID** (Unity Personal) | Free under US$200k revenue/funding; Pro when above | M0 | Engine license; needed for CI activation |
| **GitHub** (this repo) | Free / Team plan | Now | Code, CI, container registry. Git LFS storage/bandwidth beyond the free quota is paid |
| Sentry | Free developer tier → paid at scale | M2 | Crash/error reporting |
| Domain name + Cloudflare | ~US$10–20/year domain; Cloudflare free tier | M2 | API/console domains, DNS, CDN |
| VPS / server provider (e.g. Hetzner, OVH, DigitalOcean, AWS) | ~US$20–60/month at first | M2 (dev), M7 (staging) | Backend and first game servers |
| Object storage (Cloudflare R2, Backblaze B2 or S3) | A few US$/month early on | M6 | Builds, backups, Addressables |
| Transactional email (Postmark, SES or Resend) | Free tier → ~US$15/month | M2 | Verification and reset emails |
| App Store Connect API key | Included | M7 | Fastlane uploads from CI |
| Apple Push (APNs) key (.p8) | Included | M7 | Push notifications |
| macOS CI minutes (GitHub-hosted macOS runners) or Unity Build Automation | Pay per use | M7 (optional) | iOS builds without your Mac |

**Budget note:** art is the biggest variable cost. Asset Store packs cost ~US$20–500 each. Contract 3D
artists, animators and sound designers are much more, and their cost depends on scope and region.
Plan a dedicated art budget before M6.

## 3. Software on your Mac (development)

| Software | Purpose |
|----------|---------|
| **Xcode** (latest stable the pinned Unity version supports) + Command Line Tools | iOS builds, signing, Instruments, device install |
| **Unity Hub** | Manages Unity installs |
| **Unity 6 LTS** (patch pinned in M0) with modules: **iOS Build Support**, **Linux Dedicated Server Build Support** (IL2CPP), **Mac Build Support**, Documentation | Engine. The Linux server module lets you build the server on your Mac |
| **Git** + **Git LFS** | Version control (`git lfs install`) |
| **JetBrains Rider** (recommended) or **VS Code** + C# Dev Kit + Unity extension | C# IDE with Unity integration and debugger |
| **Node.js 24 LTS** (via `nvm` or `fnm`) + **pnpm** | Backend, web console, tools |
| **Docker Desktop** (or OrbStack) | PostgreSQL, Redis and the backend locally; Linux game server container |
| **.NET SDK 8 or newer** | `sim/` tests, code generation tools |
| **Fastlane** (Ruby, via Homebrew) | iOS signing and TestFlight uploads |
| **Homebrew** | Package manager for the above |
| TablePlus / DBeaver / psql | Database inspection |
| Blender (free) | Preparing and fixing 3D assets (scale, pivots, LODs) |
| An image editor (Affinity Photo, Photoshop, Krita or GIMP) | Textures, UI art |

On the iPhone: **TestFlight** app; enable **Developer Mode** (Settings → Privacy & Security) for Xcode installs.

## 4. Unity packages (installed through `Packages/manifest.json` in M0)

Universal RP, Input System, Addressables, Burst, Collections, Mathematics, TextMeshPro (included in
uGUI in Unity 6), Localization, Test Framework, Memory Profiler, Newtonsoft JSON, Profile Analyzer.
Vendored: FishNet (pinned). Later: Sentry Unity SDK, Unity IAP or a StoreKit 2 bridge, Sign in with Apple
plugin (open source).

## 5. Backend and web dependencies (installed by `pnpm install`)

Fastify and plugins, TypeBox, Drizzle ORM + drizzle-kit, postgres (driver), ioredis, BullMQ, jose,
argon2, pino, prom-client, @sentry/node, App Store Server Library, Vitest, Testcontainers, ESLint,
Prettier, TypeScript. Web: React, Vite, TanStack Router/Query, Tailwind CSS, shadcn/ui (Radix),
ECharts, React Hook Form, Playwright.

## 6. Running locally (after M0, for reference)

```bash
pnpm install
docker compose -f infra/compose/docker-compose.dev.yml up -d   # postgres, redis, (later) mail catcher, grafana
pnpm --filter @kindling/db migrate
pnpm dev                                                       # api + worker + web in watch mode
# Unity: open game/ in Unity Hub → Boot scene → Play (host or client mode)
```

## 7. What I need from you to start Milestone 0
1. Your approval (or changes) to the decisions in the [blueprint index](README.md#5-decisions-i-need-from-you).
2. Confirmation of which hardware you have (Mac? Which iPhones?).
3. The Unity 6 LTS version you installed (I'll pin it).
4. For Unity CI later: add the GitHub Actions secrets `UNITY_LICENSE` (or `UNITY_EMAIL` +
   `UNITY_PASSWORD` + serial, depending on license type), so CI can compile the Unity project and I can
   read the compile errors.
