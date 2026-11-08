# Getting KINDLING onto your iPhone for free (unsigned build + your Apple ID)

This route needs **no paid Apple Developer membership**. GitHub builds an **unsigned** `.ipa`; you then
install it with a free tool that signs it with **your own Apple ID**, the same free "personal team"
signing Xcode uses for testing apps on your own phone. It's allowed by Apple for personal use and
doesn't bypass any iOS protections.

**Trade-offs compared to TestFlight ([ios-testflight.md](ios-testflight.md)):**
- The app stops opening after **7 days**; re-install (or refresh in AltStore) to keep using it.
- A free Apple ID can have **3** sideloaded apps at a time.
- You need a **computer** (Windows or Mac) to install, and once for the Unity license.
- Only your own devices; to share with testers, use TestFlight.

> Status: this pipeline can't be tested from my side until the Unity license is added. If the first
> run fails, tell me — I can read the logs and fix it.

## Step 1 — Unity license (needs a computer once, ≈ 15 min)
Unity doesn't offer license activation from a phone; any Windows, Mac or Linux computer works, once.
1. Install **Unity Hub**, sign in (or create a free Unity account) → Preferences → **Licenses** →
   **Add** → **Get a free personal license**.
2. Open the license file as text and copy all of it:
   - Windows: `C:\ProgramData\Unity\Unity_lic.ulf`
   - macOS: `/Library/Application Support/Unity/Unity_lic.ulf`
   - Linux: `~/.local/share/unity3d/Unity/Unity_lic.ulf`

You don't need to install the Unity Editor.

## Step 2 — Add 3 secrets to GitHub (works in a phone browser)
Open **this repository's** page → **Settings** (the repository's own tab, not your profile settings) →
**Secrets and variables** → **Actions** → **Secrets** tab → **New repository secret**.
Direct link: `https://github.com/<owner>/<repo>/settings/secrets/actions`.

Add each of these as its own secret (Name + Secret, then **Add secret**):

| Secret | Value |
|--------|-------|
| `UNITY_LICENSE` | Everything in the `.ulf` file from step 1 |
| `UNITY_EMAIL` | Your Unity account email |
| `UNITY_PASSWORD` | Your Unity account password |

GitHub encrypts secrets; they never appear in logs.

**Not seen by the build:** values on the *Variables* tab, *Codespaces* or *Dependabot* secrets (including
the Codespaces secrets in your personal account settings), and secrets inside an *Environment*. When
they're in the right place, the page lists them under **Repository secrets**.

## Step 3 — Build the .ipa
**Actions** → **ios-unsigned** → **Run workflow** → version `0.1.0` → **Run**.
- The first job checks the 3 secrets and stops with a clear message if one is missing.
- The first run takes ~40–60 minutes (Unity download, project import, then compiling on a Mac).
- When it's done, open the run and download the artifact **kindling-unsigned-…** at the bottom. It's a
  zip; unzip it to get `Kindling-unsigned.ipa`.

GitHub Actions is free for public repositories. For a private repository, macOS minutes count 10×
against your plan (about 200 macOS minutes a month on the free plan; one build uses roughly 20–30).

## Step 4 — Install it on your iPhone
Pick one tool on your computer:

**Sideloadly** (Windows or Mac, simplest)
1. Install Sideloadly (sideloadly.io) and, on Windows, iTunes and iCloud from Apple's website (not the
   Microsoft Store versions).
2. Plug in the iPhone with a cable and tap **Trust** on the phone.
3. Drag `Kindling-unsigned.ipa` into Sideloadly, enter your Apple ID, click **Start**.

**AltStore** (Windows or Mac; can refresh the 7-day signature over Wi-Fi)
1. Install AltServer on the computer (altstore.io), then AltStore onto the phone from it.
2. In AltStore on the phone: **My Apps → +** → choose `Kindling-unsigned.ipa`.

(Xcode alone can't install an unsigned `.ipa`; on a Mac, Sideloadly or AltStore are still the easy way.)

Then on the iPhone (iOS 16+):
1. **Settings → Privacy & Security → Developer Mode → On** — the switch appears after the first
   install; the phone restarts and asks you to confirm.
2. **Settings → General → VPN & Device Management** → your Apple ID → **Trust**.
3. Open KINDLING. The first time you join or host a game on Wi-Fi, allow **Local Network** access.

**About your Apple ID:** Sideloadly and AltStore send your Apple ID password only to Apple to create the
free signing certificate, but they are third-party apps. If that worries you, create a second free
Apple ID just for this. Accounts with two-factor authentication work.

## Updating
Run the workflow again (bump the version if you like), download the new `.ipa` and install it the same
way; your offline world is kept (it's stored in the app's data).

## When something fails
| Symptom | Likely cause |
|---------|--------------|
| Preflight: secret missing | It isn't a repository *Actions* secret. The run's summary says where else it looked (Variables tab, misspellings, Environments); see step 2 |
| Unity step: license / activation error | `UNITY_LICENSE` isn't the full `.ulf` content, or the email/password don't match that license |
| macOS step: compile error | Tell me — I'll read the log and fix the project |
| "Unable to install" / "Untrusted developer" | Developer Mode is off, or trust your Apple ID under VPN & Device Management |
| App won't open after a week | The free signature expired: install again or refresh in AltStore |
