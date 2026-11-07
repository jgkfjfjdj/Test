# 04 — Client Architecture and iPhone Controls

## 1. One Unity project, two products

The `game/` Unity project builds:
- **iOS client**: Player build, IL2CPP, Metal.
- **Linux dedicated server**: Dedicated Server build target, IL2CPP, no rendering or audio.

Code is split into **assembly definitions** so server-only code never ships to the client and the
other way round. Each assembly declares its references explicitly; cycles are not allowed.

```
Kindling.Core          pure C#: IDs, math, fixed-point helpers, time, pooling, logging abstraction
   ▲
Kindling.Content       definition types (ItemDef, RecipeDef, BuildPieceDef, LootTableDef, SeasonDef...) + ContentDatabase
   ▲
Kindling.Simulation    pure rules: inventory ops, crafting, damage math, stability, survival stats, placement rules
   ▲                   (no UnityEngine dependency where possible → also compiled by `dotnet test`)
Kindling.Net           protocol, message types, serializers, chunk replication, FishNet adapters, tickets/resume
   ▲
Kindling.Gameplay      shared MonoBehaviours: player, entities, world, interaction, building, weather/time
   ▲            ▲
Kindling.Server   Kindling.Client
(UNITY_SERVER)    (excluded from server builds)
 authority,        input, camera, HUD, UI, audio, VFX,
 persistence,      settings, presentation of prediction,
 server link,      HUD editor
 anti-cheat,
 admin commands
                  Kindling.Backend   REST/WebSocket client, generated DTOs, auth/token refresh
                  Kindling.Platform  iOS bridges: Sign in with Apple, haptics, thermal state, App Attest, push, StoreKit
Kindling.Editor    validators, content exporter, build scripts, map tools, greybox generators
```

`Kindling.Server` uses define constraints (`UNITY_SERVER || KINDLING_SERVER_IN_EDITOR`) so it compiles
only for server builds and for in-editor testing. `Kindling.Client` is excluded from Dedicated Server
builds.

## 2. Runtime structure

- **Composition root** (`Boot` scene): creates services in a fixed order (config → logging → platform
  → backend client → content → networking → audio/UI). Services are plain C# classes registered in a
  small hand-written container. No reflection-heavy DI (IL2CPP stripping and AOT friendliness).
- **App state machine:**
  `Boot → VersionGate → Login → MainMenu (Play / Servers / Friends / Profile / Settings) →
   Connecting → LoadingWorld → InGame ⇄ Reconnecting → (Disconnected → MainMenu)`.
- **Simulation vs. presentation:** gameplay state lives in plain C# objects (component data per
  entity, with dense arrays for bulk entities). MonoBehaviours present that state (meshes, animation,
  audio, VFX) and are pooled. This separation makes the server cheaper (no presentation) and makes
  rules testable outside Unity.
- **Event bus:** typed, allocation-free events for UI updates (e.g. `InventoryChanged`,
  `StatChanged`). No string-based messaging.
- **Content database:** every definition is a ScriptableObject with a **stable string ID** and a
  **numeric network ID** (assigned by the content exporter and locked in `content-ids.lock.json`,
  so IDs never shift between builds). An editor validator stops the build on missing references,
  duplicate IDs or invalid recipes. The exporter writes `content-export.json` (items, recipes, loot
  tables), which the backend and admin console use (e.g. for item pickers in inventory moderation).

## 3. Touch controls

### 3.1 Default layout (landscape, safe area respected)

```
┌───────────────────────────────────────────────────────────────────────────┐
│ [HP][FOOD][WATER][WARMTH]           ─── compass N ───        [MAP] [MENU] │
│ [status effects]                                             [CHAT] [TEAM]│
│                                                                           │
│                                 ·  (crosshair)                            │
│                          [ Interact: Open Chest ]                         │
│                                                              [AIM]        │
│     ╭───────────╮                                    [RELOAD]  ╭───────╮  │
│     │ joystick  │                                              │ATTACK │  │
│     │  (float)  │                                    [CROUCH]  ╰───────╯  │
│     ╰───────────╯                                         [JUMP]          │
│ [SPRINT]          [1][2][3][4][5][6]  [BAG]                     [BUILD]   │
└───────────────────────────────────────────────────────────────────────────┘
   ◄── movement zone (left ~40%) ──►◄────────── look zone (rest of screen) ─────►
```

### 3.2 Control behavior

| Control | Behavior | Options |
|---------|----------|---------|
| **Virtual joystick** | Floating: appears where the left thumb lands inside the movement zone. Dead zone 10%; analog walk speed | Fixed / floating; size; dead zone; opacity |
| **Sprint** | Push the joystick past 95% forward → sprint. Slide up into the **sprint-lock** zone above the stick → auto-sprint until you pull back. Also a toggle button | Auto-sprint on/off; button visible |
| **Camera look** | Drag anywhere in the look zone that isn't a button. Multi-touch: look still works while the joystick and a button are held | X/Y sensitivity, aim (ADS) multiplier, invert Y, acceleration curve (off by default) |
| **Gyro aim** | Device rotation adds to the camera | Off / while aiming / always; sensitivity |
| **Attack** | Tap for melee and single shots; hold for automatic or to charge a bow. **Drag while holding** also steers the camera, so one thumb can aim and fire | Button size and position |
| **Aim (ADS)** | Tap toggles, hold for hold-to-aim | Toggle / hold |
| **Jump / Crouch** | Buttons. Crouch: tap toggles, hold for momentary | |
| **Interact** | Context button that appears only when a valid target is in focus and shows the action label. Hold = pick up (grab system); long-press = radial menu | Tap-object-to-interact on/off |
| **Hotbar** | 6 slots. Tap to equip, swipe across slots to switch, long-press for item actions | 4–6 slots shown |
| **Inventory (BAG)** | Opens the inventory/crafting screen (game keeps running; you're vulnerable) | |
| **Build** | Shows only while a building plan or mallet is equipped (see 3.4) | |
| **Reload** | Shows only while a weapon is equipped | |

**Touch handling rules:**
- Each finger is owned by the first control it touches, until it lifts (no stealing between controls).
- Input is processed in `Update` and sent with the next network tick, so input lag stays at one frame
  + one tick at most.
- `Input.multiTouchEnabled` is on; system edge gestures are deferred
  (`preferredScreenEdgesDeferringSystemGestures`) so swipes near the edges don't open Control Center by
  accident.
- **Haptics** (Core Haptics bridge): weapon fire, hit confirm, gathering impacts, low-health heartbeat,
  UI confirms. Intensity setting, off switch.

### 3.3 Inventory UX on touch
- Grid with large slots (at least 44 pt, per Apple's minimum hit target).
- **Tap** a slot → item detail and action buttons (Use, Equip, Split, Drop).
- **Long-press + drag** to move between slots and containers. **Double-tap** to quick-move between
  inventory and an open container.
- Split slider for stacks. Crafting tab: recipe list (filtered by station in range), material
  checklist, queue view.
- Looting: a split view with the container on the left and your inventory on the right. A "Take all"
  button.

### 3.4 Build mode on touch
1. Equip the Building Plan → the **piece radial** opens (foundation, wall, doorway...).
2. A ghost preview follows the crosshair and snaps to sockets. It turns green or red, with a reason
   ("Not enough Wood", "Blocked by claim", "Unstable").
3. **Rotate** button (90° steps; free rotation for deployables), then the **Place** button. You never
   place by tapping, which prevents accidental placements.
4. Mallet: aim at a piece → radial: Upgrade (tier list with costs), Repair, Rotate, Demolish (only
   within 10 min of placement and only if authorized).

### 3.5 Customizable HUD
- **HUD Editor** (Settings → Controls → Edit HUD): drag any element, pinch to resize (50–150%), set
  per-element opacity (20–100%), hide optional elements, snap to a grid, reset to default.
- Positions are stored as anchors normalized to the safe area, so a layout carries over between screen
  sizes. Each layout is saved per device class (phone/tablet), synced to the cloud save, and you can
  keep up to 3 presets.
- Overlap and safe-area checks stop you saving a layout where elements can't be reached.

### 3.6 Game controllers
MFi, Xbox and DualSense controllers through the Input System. When one connects, the touch overlay
hides and a controller layout applies. Controller players get **no aim assist**. Servers can show
"touch-only" as a tag, but because input method can't be verified, this is a social label, not
enforcement.

## 4. Settings

| Category | Settings |
|----------|----------|
| Controls | Look sensitivity X/Y, aim multiplier, invert Y, acceleration, gyro mode and sensitivity, auto-sprint, crouch/aim toggle or hold, joystick fixed/floating, haptics intensity, HUD Editor |
| Graphics | Quality preset (**Auto**, Low, Medium, High), frame-rate cap (30/60), render scale (50–100%), shadows (off/low/medium/high), view distance, vegetation density, texture quality, effects quality, post-processing on/off, anti-aliasing (off/FXAA/MSAA 2×), **Battery Saver** (30 FPS + 75% render scale), **Thermal auto-adjust** (on by default) |
| Audio | Master, effects, music, ambience, voice (later); mono audio |
| Gameplay | FOV (60–90°), crosshair style, damage numbers, aim assist (touch), language |
| Accessibility | Colorblind filters (protan/deutan/tritan), text size, subtitles, high-contrast HUD, reduce camera shake, reduce flashing |
| Privacy | Analytics opt-out, push notification categories, who can send friend requests or join you |
| Account | Linked logins, email, **Delete account** (required by Apple), sign out |

## 5. Native iOS bridges (`Kindling.Platform` + `Assets/Plugins/iOS`)

| Bridge | API | Used for |
|--------|-----|----------|
| Sign in with Apple | AuthenticationServices | Login |
| App Attest / DeviceCheck | DCAppAttestService, DCDevice | Integrity signal at login; per-device ban bits |
| Haptics | Core Haptics | Feedback |
| Thermal state | `ProcessInfo.thermalState` notifications | Dynamic quality scaling |
| Push | UNUserNotificationCenter + APNs registration | Raid alerts, invites |
| StoreKit 2 | via Unity IAP or a thin bridge | Cosmetics (post-launch) |
| Low Power Mode | `ProcessInfo.isLowPowerModeEnabled` | Suggest Battery Saver |

## 6. Backend communication from the client
- `Kindling.Backend` wraps `UnityWebRequest` with JSON (Newtonsoft JSON, AOT-safe settings), retries
  with exponential backoff for idempotent calls, and automatic access-token refresh.
- DTOs are **generated from the backend's OpenAPI spec** (`tools/codegen`), so client and server stay
  in sync.
- WebSocket (`/v1/realtime`): `System.Net.WebSockets.ClientWebSocket` (works under IL2CPP on iOS),
  with heartbeats, automatic reconnect, and presence and notification event handling.

## 7. Graphics tiers and performance budgets

### 7.1 Device tiers (detected automatically; the player can override)

| Tier | Devices (chip) | Target | Render scale | Shadows | View distance |
|------|----------------|--------|--------------|---------|---------------|
| **Low** | A13 (iPhone 11 series, SE 2nd gen) | 30 FPS | 70–80% | 1 cascade, short | 250 m |
| **Medium** | A14–A15 (iPhone 12, 13, 14, SE 3rd gen) | 30 FPS (60 optional) | 80–90% | 1–2 cascades | 400 m |
| **High** | A16 and newer (iPhone 14 Pro, 15 series and later) | 60 FPS | 85–100% | 2 cascades, soft | 600 m |

Detection uses the device model plus a ~3-second GPU/CPU micro-benchmark on first launch. At runtime,
thermal state and frame time adjust render scale and effects (with hysteresis so quality doesn't
flicker).

**Minimum device:** iPhone 11 / SE (2nd gen) (A13). At launch, the minimum iOS version will likely be
set so that it allows only A13+ devices (iOS 26, for example, supports iPhone 11 and later). A12
devices are excluded unless M6 profiling shows they can hold 30 FPS on Low. The exact minimum iOS
version is set in M10 based on adoption numbers at that time.

### 7.2 Frame budgets (starting values, refined in M1/M6)

| Budget | Low (30 FPS) | High (60 FPS) |
|--------|--------------|---------------|
| Frame time | 33.3 ms (target ≤ 28 ms for thermal headroom) | 16.6 ms (target ≤ 14 ms) |
| Main thread CPU | ≤ 18 ms | ≤ 10 ms |
| SetPass calls / draw calls | ≤ 120 / ≤ 300 | ≤ 200 / ≤ 600 |
| Visible triangles | ≤ 350 k | ≤ 900 k |
| App memory (total) | ≤ 1.4 GB (on 3–4 GB devices) | ≤ 2.2 GB |
| Texture memory | ≤ 450 MB | ≤ 900 MB |
| GC allocations per frame (gameplay) | **0 B** in steady state | 0 B |

### 7.3 Techniques
- SRP Batcher, GPU instancing for vegetation and repeated props; static batching for points of interest.
- LOD groups everywhere. **Impostors** (billboards) for distant trees. Terrain split into tiles with
  lower-detail far tiles.
- Occlusion culling baked for interiors and points of interest; the far plane is managed by view
  distance + fog.
- **Building pieces** are combined into per-chunk meshes when they don't change (re-combined on change),
  so they cost far fewer draw calls.
- ASTC texture compression; mipmap streaming; texture atlases for UI.
- Shader variant stripping + shader warm-up during the loading screen, to avoid first-use hitches.
- Burst jobs for vegetation culling and animation LOD; animation culling for distant players.
- Audio: compressed (AAC/Vorbis), streaming for music, a limit on concurrent voices.
- Memory: Addressables reference counting; unload unused terrain tiles. Request the
  **Increased Memory Limit** entitlement. Respond to memory warnings (drop caches, lower texture
  quality).
- Declare the app as a game so iOS Game Mode can apply on supported OS versions.

## 8. App lifecycle on iOS
- `OnApplicationPause(true)` → send `going_background`, flush the cloud save, pause audio.
- Resume → check the connection; use the resume token within 120 s; otherwise re-ticket and rejoin.
- Lost network → "Reconnecting…" overlay with automatic retries (2, 4, 8, 16 s), then the main menu
  with a **Resume** button.
- Low memory warning → release pooled objects, unload unused Addressables, reduce texture quality.

## 9. Audio design (technical)
- Unity audio with mixer groups (SFX, ambience, music, voice). Distance-based attenuation.
  Simple occlusion (one raycast, low-pass filter) for doors and walls. Footstep surfaces taken from
  terrain layers and materials.
- Sound is a core part of PvP awareness, so it gets dedicated mixing and a "headphones recommended" tip.

## 10. Localization
English at launch. All UI text goes through Unity Localization string tables from day one, so adding a
language later is mostly translation work. Fonts are TextMeshPro with fallbacks for wider character
sets.
