# Getting KINDLING onto your iPhone (TestFlight) — no Mac required

> **No paid Apple account?** There's a free route: an unsigned build you install with your own Apple
> ID (re-install every 7 days) — [ios-sideload.md](ios-sideload.md).

The repository can now build the iPhone app by itself on GitHub's machines:

```
GitHub Actions ─ ios-testflight workflow
 1. Linux runner:  Unity 6000.3.27f2 exports the Xcode project        (needs your Unity license)
 2. macOS runner:  fastlane signs it, builds Kindling.ipa, uploads it   (needs your Apple account)
 3. TestFlight:    you install it from the TestFlight app on your iPhone
```

**What's in the build right now:** a pre-alpha technical build. It opens to a screen showing the
version, your device and the frame rate — **there is no gameplay yet** (that's Milestone 1). It proves
the whole chain works: Unity → signed app → TestFlight → your phone.

**Why I (Claude) can't just hand you an .ipa:** Apple only signs iPhone apps for a paid Apple Developer
account holder, and Unity only builds for a licensed Unity account. Both accounts must be yours
(identity check and payment). Once they exist, every build is one button.

## What it costs
- **Apple Developer Program:** US$99 per year (required by Apple for TestFlight and the App Store).
- **Unity Personal:** free (while revenue/funding is under US$200k per year).
- **GitHub Actions:** free for public repositories, including the macOS runners. If you make the repo
  private, macOS minutes count 10× against your plan's minutes (~200 macOS minutes on the free plan;
  one iOS build uses roughly 15–30).

## Step 1 — Apple Developer Program (≈ 15 min + Apple's review, up to 48 h)
1. On your iPhone, install the **Apple Developer** app → **Account** → **Enroll now**
   (or use developer.apple.com/programs/enroll in a browser). Enroll as an **individual** unless you
   have a company with a D-U-N-S number.
2. When approved, open developer.apple.com/account → **Membership details** and note your
   **Team ID** (10 characters, e.g. `ABCDE12345`). → secret `APPLE_TEAM_ID`.

## Step 2 — Register the app id (5 min, browser)
1. developer.apple.com/account → **Certificates, IDs & Profiles** → **Identifiers** → **+** →
   **App IDs** → **App**.
2. Description `Kindling`; **Bundle ID: Explicit**, e.g. `com.yourname.kindling` (must be unique
   worldwide; use your own reverse domain or name). No capabilities needed yet. → secret `IOS_BUNDLE_ID`.

## Step 3 — Create the app record in App Store Connect (5 min, browser)
1. appstoreconnect.apple.com → **Apps** → **+** → **New App**.
2. Platform **iOS**; Name: any unique name for now (e.g. "Kindling Survival" — App Store names are
   unique; you can rename before release); language; **Bundle ID** from step 2; SKU `kindling-ios`;
   access **Full Access**.

## Step 4 — App Store Connect API key (5 min, browser)
1. appstoreconnect.apple.com → **Users and Access** → **Integrations** → **App Store Connect API** →
   **Team Keys** (click "Request Access" the first time) → **Generate API Key**.
2. Name `github-ci`, access **Admin** (needed to create the signing certificate and profile).
3. Note the **Key ID** → secret `ASC_KEY_ID`, and the **Issuer ID** at the top → secret `ASC_ISSUER_ID`.
4. **Download** the `AuthKey_XXXXXXXXXX.p8` file — Apple lets you download it **only once**. Open it
   as text and copy everything (including the `-----BEGIN PRIVATE KEY-----` lines) → secret `ASC_KEY_P8`.

## Step 5 — A private repository for signing certificates (5 min, github.com)
fastlane "match" stores your Apple signing certificate there, encrypted.
1. github.com → **New repository** → name `kindling-certificates` → **Private** → create (empty).
   → secret `MATCH_GIT_URL` = `https://github.com/<your-username>/kindling-certificates.git`
2. github.com → Settings → Developer settings → **Fine-grained personal access tokens** → Generate:
   repository access **Only select repositories → kindling-certificates**; permissions
   **Contents: Read and write**; expiry as you like (you'll need to renew it).
   → secret `MATCH_GIT_TOKEN`; your GitHub username → secret `MATCH_GIT_USERNAME`.
3. Choose a strong passphrase that encrypts the certificates → secret `MATCH_PASSWORD`
   (keep it in a password manager; without it the stored certificates are unusable).

## Step 6 — Unity license (needs a computer once, ≈ 15 min)
Unity doesn't offer license activation from a phone; any Windows, Mac or Linux computer works, once.
1. Install **Unity Hub**, sign in with your Unity account → Preferences → **Licenses** → **Add** →
   **Get a free personal license**.
2. Open the license file as text and copy all of it → secret `UNITY_LICENSE`:
   - Windows: `C:\ProgramData\Unity\Unity_lic.ulf`
   - macOS: `/Library/Application Support/Unity/Unity_lic.ulf`
   - Linux: `~/.local/share/unity3d/Unity/Unity_lic.ulf`
3. Your Unity account email → `UNITY_EMAIL`, password → `UNITY_PASSWORD`.
   (Unity Pro instead: set `UNITY_SERIAL` and skip `UNITY_LICENSE`.)

You don't need to install the Unity Editor — CI pins Unity 6000.3.27f2 and its exact package versions.

## Step 7 — Add the secrets to GitHub (works in a phone browser)
This repository → **Settings** → **Secrets and variables** → **Actions** → **New repository secret**,
once per name:

| Secret | From |
|--------|------|
| `APPLE_TEAM_ID` | Step 1 |
| `IOS_BUNDLE_ID` | Step 2 |
| `ASC_KEY_ID`, `ASC_ISSUER_ID`, `ASC_KEY_P8` | Step 4 |
| `MATCH_GIT_URL`, `MATCH_GIT_USERNAME`, `MATCH_GIT_TOKEN`, `MATCH_PASSWORD` | Step 5 |
| `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` | Step 6 |

Secrets are encrypted by GitHub and are never shown in logs or to pull requests from forks.

## Step 8 — Build
**Actions** → **ios-testflight** → **Run workflow** → version `0.1.0` → **Run**.
- The first job prints a checklist of the secrets (✅ / ❌). If anything is missing it stops there.
- The first full run takes ~30–45 minutes (Unity download and project import); later runs are faster.
- When it finishes, the `.ipa` is attached to the run (artifact `kindling-ios-…`) and the build is
  uploaded to TestFlight. Apple then processes it for ~10–30 minutes.

An App Store-signed `.ipa` can't be installed by tapping it — iPhones install it through TestFlight
(or, later, the App Store).

## Step 9 — Install on your iPhone
1. App Store Connect → your app → **TestFlight** → **Internal Testing** → **+** create a group →
   add yourself (your Apple account is already a user). Internal testing needs no Apple review.
2. Install **TestFlight** from the App Store on your iPhone, open the invitation email (or the
   TestFlight app) and install **KINDLING**.

## When something fails
Tell me — I can read the workflow logs on GitHub and fix the cause. Common first-run issues:

| Symptom | Likely cause |
|---------|--------------|
| Preflight ❌ | A secret is missing or misspelled |
| Unity step: license / activation error | `UNITY_LICENSE` is not the full `.ulf` content, or the email/password don't match that license |
| match: "Could not find App ID" | The bundle id in `IOS_BUNDLE_ID` isn't registered (step 2) or differs in spelling |
| match: permission / 403 | The API key isn't **Admin**, or the token can't write to the certificates repo |
| Upload: "No suitable application records" | The App Store Connect app (step 3) doesn't exist or uses a different bundle id |
| Upload: build number already used | Re-run the workflow (each run gets a new build number) |
| Upload rejected: SDK too old | Apple requires a newer Xcode than the runner's default; I'll pin a newer macOS runner |
