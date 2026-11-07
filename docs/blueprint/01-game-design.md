# 01 — Game Design Overview

This document sets the **design targets** that the technical architecture has to support. Numbers here
are starting values. All balance lives in data (ScriptableObject definitions and server config), so it
can be tuned without code changes.

## 1. Pitch

You wake up on a cold northern coastline with nothing. You gather with your bare hands, craft your first
tools, claim land and build a base that stays in the world, and decide whether the strangers you meet are
allies or targets. Everything happens in first person and feels physical. Any base can be raided. Every
server is its own community.

- **Setting (original):** "The Reach", a fictional temperate-to-subarctic coastal region that was
  abandoned for reasons nobody explains. Ruined industry and wilderness, with a light thread of mystery.
- **Art direction (recommended):** *stylized realism*. Clean shapes, strong lighting, restrained
  texture detail. It's cheaper for a small team to produce, runs well on mobile GPUs and ages well.

## 2. Design pillars

| Pillar | What it means in practice |
|--------|---------------------------|
| **Hands-on survival** | The world is touchable. You pick up, carry, place and throw objects, work stations by hand, and see your own hands and tools. Immersion comes through touch and sound, not menus. |
| **Earned progress, real risk** | Everything you own was gathered, crafted or taken. Anything you own can be lost. Wipes reset the world on a regular cycle. |
| **Social by default** | Proximity text (and later voice), gestures, teams, signs, trading spots. Players make the stories. |
| **Built for iPhone, not ported** | Sessions of 15–40 minutes, controls designed for thumbs, offline-raid protection options, fast resume after interruptions, battery and data awareness. |
| **Fair and authoritative** | The server decides everything that matters. No pay-to-win. Moderation tools ship from the first public test. |

## 3. Inspiration → original implementation

| Inspired by (genre trait) | Our original take |
|---------------------------|-------------------|
| Physical object interaction in VR survival | **Grab system**: hold to pick up props into your hands, drag to rotate, flick to throw, tap to place with surface snapping. Heavy loot crates must be *physically carried* home. |
| Social VR presence | Visible first-person hands, full-body avatars for other players, an emote/gesture wheel, proximity chat bubbles, optional push-to-talk proximity voice (post-MVP decision). |
| Community-driven VR servers | Community-hosted servers with owner tools, server events, signs and notice boards (moderated UGC). |
| PC open-world survival: gather/craft/build/raid | Our own resource chain, tech tree, building tiers, stability model, claim system ("Claim Beacon"), lock types, explosives and decay/upkeep. All numbers and names are original. |
| Persistent servers with wipes | Wipe schedules set per server; account-level cosmetic progression survives wipes. |

**Originality rules (they apply to everyone on the project):**
- No code, assets, names, item lists, icons, maps, sounds or UI layouts copied from any other game.
- Generic real-world materials (wood, stone, iron, coal) and genre conventions (foundations, walls,
  doors) are fine. Proprietary names and distinctive designs are not.
- Every third-party asset needs a license that allows commercial use in a mobile game. We keep a record
  of these in `docs/licenses/`.

## 4. Core loops

**Moment-to-moment:** look → move → interact (gather, loot, fight, build) → manage inventory.

**Session loop (mobile, 15–40 min):**
1. Open the app, then *Resume* (rejoin your last server in one tap).
2. Check your base: raid log, upkeep in the Claim Beacon, chests.
3. Pick an objective: gather a resource, loot a ruin, craft an upgrade, meet your team.
4. Come home, store your loot, log out safely inside your claim (see Sleepers, §6.4).

**Wipe loop (days to weeks):** fresh spawn → primitive tools → first base → metal tier → raids and
conflict → endgame tools → wipe → start again.

**Meta loop (months):** account level and seasonal challenges unlock *cosmetics only*. Stats and history
build your profile. New seasons rotate content.

## 5. World

### 5.1 Map
- Launch map: **2 km × 2 km**, built with in-editor procedural tools and then finished by hand. It ships
  inside the app binary.
- Each server sets a **seed** that varies the active resource nodes, loot spawns and hidden caches on
  the same map.
- The engine supports maps up to 4 km × 4 km. Fully procedural, server-generated maps are post-launch
  research (§17).

### 5.2 Biomes

| Biome | Look | Resources | Gameplay role |
|-------|------|-----------|---------------|
| Coastal beaches | Sand, driftwood, tide pools | Driftwood, fiber, shellfish, salvage from wrecks | Safe-ish spawn area, low-tier loot |
| Temperate forest | Dense conifers and birch | Wood (lots), fiber, berries, mushrooms, deer, boar | Early game, good hidden base spots |
| Rivers and lakes | Freshwater, reeds | Clean water, fish, clay | Water source, natural borders |
| Highland meadows | Open grass, rocky outcrops | Stone, small iron nodes, herbs | Contested open ground |
| Mountains | Cliffs, scree, snowline | Large stone/iron/coal nodes, saltpeter (rare) | Mid/late resources, cold, falling danger |
| Caves | Narrow, dark, branching | Saltpeter, rich iron, crystals, hidden caches | High value, high risk, needs a light source |
| Blighted zones | Dead vegetation, haze | Salvage-rich ruins | Health drain without protective gear |

### 5.3 Points of interest (abandoned structures)

| Tier | Examples (original) | Loot | Hazard |
|------|---------------------|------|--------|
| Low | Ranger Station, Fishing Shacks, Roadside Wrecks | Basic components, food, cloth | Animals |
| Mid | Lumber Mill, Quarry Works, Rail Depot, Shipwreck | Salvage, tool parts, ammunition | Exposure, other players |
| High | Hydro Dam, Radio Relay, Fish Cannery (blighted) | Weapon parts, Blast Powder components, tech items | Blight, traps, PvP hot zones |

Each point of interest has fixed loot-spawn markers ("loot spawners") that refill on a timer. Spawners
roll against data-driven loot tables.

### 5.4 Hidden loot
- **Caches:** small stashes hidden in caves, under rocks and in wreck interiors. Only replicated to a
  client within ~5 m (interest management makes them invisible to ESP-style cheats at range).
- **Player stashes:** buryable containers that can't be seen unless a player is within 2 m.
- **Salvage crates:** heavy, *carried* containers (grab system) that need a bench to open, which forces a
  risky trip home.

### 5.5 Environmental hazards

| Hazard | Effect | Counter |
|--------|--------|---------|
| Cold (night, mountains, water, weather) | Warmth drops → health drain, slower stamina regen | Clothing insulation, fire, shelter |
| Heat (sun exposure in summer weather) | Faster thirst | Shade, water |
| Drowning / cold water | Breath meter; cold | Surface, warm up |
| Falling | Damage above 4 m, scaled by height | Care, climbing |
| Blight zones | Exposure builds up → health drain | Protective suit tiers, tonics |
| Wildlife | Wolves and boars attack; bears are rare | Weapons, fire, avoiding them |
| Storms | Visibility, cold, lightning risk on peaks (later) | Shelter |

## 6. Survival systems

### 6.1 Player stats

| Stat | Range | Drains from | Effect when critical |
|------|-------|-------------|----------------------|
| Health | 0–100 | Damage, starvation, dehydration, cold, bleeding, blight | 0 → **Downed** (30 s; a teammate can revive) → Death |
| Hunger | 0–100 (100 = full) | Time; faster when sprinting or cold | < 20: no health regen. 0: health loss |
| Thirst | 0–100 | Time; faster when hot or sprinting | < 20: stamina penalty. 0: health loss |
| Warmth | −100 … +100 (comfort band −20 … +20) | Weather, biome, time of day, wetness, clothing | Cold: health drain, slower actions. Hot: thirst drain |
| Stamina | 0–100 | Sprinting, jumping, melee swings | 0: can't sprint or swing until partly recovered |
| Exposure | 0–100 | Blight zones | Over 50: health drain that scales with exposure |

**Status effects:** Bleeding, Wet, Cold, Hot, Poisoned (bad food), Well-fed (regen bonus), Comfort (near
a fire inside a claim: faster regen).

### 6.2 Food and water
- Food: berries, mushrooms (some poisonous), raw and cooked meat, fish, canned goods from ruins, farmed
  crops (post-MVP).
- Water: lakes and rivers (drink directly, small poison chance from stagnant water), collected rain
  (Rain Barrel), boiled water (campfire + container), canned water from loot.

### 6.3 Death and respawn
- On death, the character leaves a **body container** with its inventory (worn items included), which
  decays after 10 minutes.
- Respawn choices: a random coastal spawn, or any **Bedroll/Bed** you own (cooldown 5 min / 2 min).
- Respawning gives a starter kit (torch, stone). Server config can change this.

### 6.4 Sleepers (logging out)
When a player disconnects, their character stays in the world asleep. The server owner picks how
sleepers work:

| Mode | Behavior |
|------|----------|
| `vulnerable` (classic) | Sleepers can be killed and looted anywhere |
| `protected-in-claim` (**official default**) | Sleepers inside a claim where they are authorized can't take damage. Outside a claim they're vulnerable |
| `vanish` | Characters disappear after 60 s offline and reappear on login (casual/PvE servers) |

## 7. Items, inventory and equipment

- **Inventory:** 24 slots. **Hotbar:** 6 slots. **Equipment:** head, chest, legs, feet, hands, back
  (a backpack adds 6–12 slots).
- Every item has: a stable string ID (`item.tool.iron_axe`), a numeric network ID, a category, a stack
  size, weight (affects carrying), condition/durability (tools, weapons, armor) and optional data
  (ammo loaded, skin ID).
- Item operations — move, split, merge, drop, use, equip, craft — are **commands sent to the server**.
  The client predicts nothing in the inventory. The UI shows a pending state for up to ~150 ms, then
  takes the server's result. The rules live in shared C# code so the UI can pre-check them.

## 8. Gathering

| Node | Tool tiers | Yields |
|------|------------|--------|
| Trees (sizes S/M/L) | Hands → Stone Hatchet → Iron Axe | Wood (+ chance of resin) |
| Stone outcrops | Rock → Stone Pick → Iron Pick | Stone |
| Iron / coal / saltpeter nodes | Stone Pick (slow) → Iron Pick | Ores |
| Plants (fiber, herbs, berries) | Hands | Fiber, food, herbs |
| Animals | Any weapon, then harvest the corpse | Meat, hide, bone, fat |
| Salvage barrels and wrecks | Melee to break | Salvage, components |
| Driftwood, loose rocks | Hands (grab system) | Small amounts; good for early game |

- Hitting a "sweet spot" marker on a tree or rock gives bonus yield (a light skill element that works
  well on touch).
- Depleted nodes respawn on server-configured timers. Their state replicates per chunk as a compact
  bitset (see [05](05-multiplayer-networking.md)).
- Server gather multipliers: 1×–10×.

## 9. Crafting and tech progression

- **Crafting queue:** crafting takes real time and keeps going while you move. Cancel refunds the
  materials.
- **Stations:** Hand → **Crude Bench** (T1) → **Forge Bench** (T2) → **Machinist Bench** (T3). Plus the
  Campfire (cook, boil), **Smelter** (ore → bars, wood → charcoal) and **Tinker Bench** (learn blueprints).
- **Knowledge:** most T2+ recipes have to be *learned*. You consume a found item plus Salvage at a
  Tinker Bench, or buy the recipe step on a T1–T3 bench's tree with Salvage. Knowledge resets on a
  server's *blueprint wipe* (owner-configured).

| Tier | Representative items (original set; final list in content data) |
|------|------------------------------------------------------------------|
| T0 Primitive | Stone Hatchet, Stone Pick, Wooden Spear, Torch, Shortbow, Arrows, Bandage, Bedroll, Campfire, Small Chest, Frame building pieces |
| T1 Crude | Bone Knife, Hide armor, Crossbow, Timber Door, Tumbler Lock, Large Chest, Smelter, Rain Barrel |
| T2 Forge | Iron Axe, Iron Pick, Machete, Iron armor pieces, Revolver, Breech Shotgun, Iron Door, Keypad Lock, Fire Bottle |
| T3 Machinist | Lever Rifle, Salvaged SMG, Reinforced Door, Crude Breach Charge, Breach Charge, Blight Suit |

Resource chain (original):
`Wood, Stone, Fiber, Hide, Bone, Fat, Iron Ore → Iron Bar, Coal (mined or charcoal), Saltpeter (rare) →
Blast Powder (Coal + Saltpeter), Salvage (from ruins), Cloth (Fiber), Rope (Fiber), Lamp Oil (Fat + Coal).`

## 10. Building

### 10.1 Pieces
- **Structure:** Foundation (square and triangle), Floor (square and triangle), Wall, Half-Wall, Doorway,
  Window Frame, Stairs (L, U), Ramp, Roof (flat, pitched).
- **Perimeter:** Palisade Wall, Palisade Gate, Stone Rampart, Rampart Gate.
- **Openings:** Timber/Iron/Reinforced Doors (single and double), Window Bars, Window Shutters.
- **Locks:** **Tumbler Lock** (crafted with a matching key, keys can be copied) and **Keypad Lock**
  (4-digit code plus an authorized list; wrong guesses trigger a lockout and an electrical shock).
- **Deployables:** chests, benches, smelter, campfire, beds, rain barrel, shelves, signs, furniture,
  lights, decorations (seasonal ones included).

### 10.2 Placement rules
- Grid of 3 m × 3 m, walls 3 m tall. Pieces snap to sockets (edges, corners, tops).
- The server re-runs every placement check: sockets, collisions, terrain slope, distance from
  restricted zones (points of interest, spawn areas), claim permissions and cost.
- **Build-free zones** around points of interest and road corridors stop blocking.

### 10.3 Tiers and upgrades

| Tier | HP (starting value) | Upgrade cost (per wall, starting value) | Notes |
|------|---------------------|------------------------------------------|-------|
| Frame | 40 | Placement only (Wood ×20) | Temporary; easily destroyed by hand |
| Timber | 250 | Wood ×200 | Weak to fire |
| Stone | 500 | Stone ×300 | Main mid-game tier |
| Iron | 1 000 | Iron Bar ×200 | |
| Reinforced | 2 000 | Iron Bar ×25 + Salvage ×15 | Endgame |

Upgrading is done with the **Builder's Mallet**: tap a piece, then pick a tier from the radial menu.

### 10.4 Stability
- Foundations have stability 1.0. A piece directly on top of a supported piece keeps its supporter's
  stability. Each horizontal span off the support path multiplies by 0.75.
- You can't place a piece with stability below 0.1. When a supporting piece is destroyed, the server
  recalculates the affected group incrementally, and anything at 0 collapses with a short delay and
  debris VFX.
- Large recalculations run in a Burst job off the main simulation step.

### 10.5 Claim Beacon, permissions and upkeep
- Placing a **Claim Beacon** creates a claim that covers the connected building plus a 15 m margin.
  Authorized players can build, upgrade, repair and rotate pieces inside it. Nobody else can.
- Raiders must destroy the beacon to take over the claim. It is the base's heart.
- **Upkeep:** the beacon has storage. Every hour it uses materials based on the building's size and
  tiers. With no upkeep, the building **decays** at a rate per tier. This keeps the world from filling
  up with abandoned bases.
- Authorization list: up to 20 accounts. Changes are logged in the claim's activity log.

## 11. Raiding and loot protection

| Damage type | Good against | Examples |
|-------------|--------------|----------|
| Blunt melee | Frame, Timber (slow) | Sledge, rocks |
| Fire | Timber pieces, timber doors | Fire Bottle, flame bolts |
| Explosive | All tiers | Crude Breach Charge (cheap, weak), Breach Charge (expensive, strong) |
| Projectile | Doors and deployables (weakly) | Bullets, bolts |

- Doors and windows are the weak points. Walls are the strong points. Raid cost is tuned so a
  reinforced core needs a meaningful investment of Blast Powder.
- **Loot protection:**
  - A locked container can only be opened by authorized players or with the right code or key.
  - Destroying a container drops **50%** of its contents (the rest is destroyed). Server-configurable.
  - Hidden stashes give an alternative to obvious chests.
- **Raid awareness (mobile-friendly):**
  - The **claim activity log** records which pieces were damaged or destroyed and when.
  - **Push notifications** (opt-in) are sent when your claim takes explosive damage. They're rate-limited
    to one per claim every 10 minutes.
- **Offline raid protection** (server config, see [06](06-dedicated-servers.md)):
  `offlineDamageMultiplier` (0–1) applies once *every* authorized player has been offline for
  `offlineGraceMinutes`. Optional **raid windows** limit raiding to certain hours.
  Official default: 0.5 multiplier, 15 min grace.

## 12. Combat

- **Weapons:** melee (spear, machete, sledge), bows (shortbow, crossbow), firearms (revolver, breech
  shotgun, lever rifle, salvaged SMG), throwables (fire bottle, breach charges).
- **Damage model:** per-hitbox multipliers (head ×1.8, limbs ×0.7). Armor reduces damage by type.
  Weapon condition affects reliability.
- **Ballistics:** arrows, bolts and bullets are server-simulated projectiles with drop and speed
  (rifles are near-hitscan within 50 m). Hits use lag compensation ([05 §6](05-multiplayer-networking.md#6-lag-compensation-and-hit-validation)).
- **Touch-fair time-to-kill:** longer than PC shooters, so touch players can react. Tuned in data.
- **Aim assist:** touch input only. *Slowdown* near targets, never auto-snapping. Servers can turn it
  off. Gamepad players get no aim assist.

## 13. Physical interaction (grab system)

| Verb | Touch input | Server behavior |
|------|-------------|-----------------|
| Use / open | Tap the Interact button (or tap the highlighted object) | Validated: distance ≤ 3 m, line of sight, permissions |
| Pick up into hands | Hold Interact for 0.4 s on a grabbable prop | Prop attaches kinematically to the player (no physics while held) |
| Rotate held object | Two-finger twist / drag on the right side | Client-side preview; final transform checked on place |
| Place | "Place" button; snaps to surfaces | Server checks collision and claim rules |
| Throw | Flick upward on the right side | Server simulates the rigidbody until it sleeps, then turns it back into a chunk entity |
| Radial actions | Long-press an object | Context menu: pick up, rotate, open, lock, upgrade... |

- Heavy props (logs, salvage crates) slow you down and block weapon use: carrying is a commitment.
- Physics is limited on purpose: a cap per server (e.g. 200 active rigidbodies) and auto-sleep after
  3 s of rest.

## 14. Social features

- Proximity text chat with world-space bubbles, team chat and global server chat (owner can turn it off).
- **Teams** of up to 4 (server-configurable): shared map markers, see each other through walls on the
  compass, no friendly fire (configurable).
- **Gesture wheel:** wave, point, follow me, thumbs up, surrender, trade.
- **Signs and notice boards:** player text. It goes through the moderation filter and can be reported.
- **Trading:** a two-person trade window with a confirmation step (no remote trading, both players must
  be within 3 m).
- **Proximity voice:** push-to-talk, off by default, behind a feature flag. Go/no-go after M7
  ([15](15-risks-and-limitations.md)).
- Friends, presence and "join friend" through the platform (see [07](07-backend.md)).

## 15. Day/night and weather

- Default cycle: **60 minutes** (45 min day, 15 min night). Configurable per server.
- The server is the authority for time of day and the weather state. Clients render them locally from
  low-rate updates.

| Weather | Visual | Gameplay |
|---------|--------|----------|
| Clear | Sun, long shadows | Normal |
| Overcast | Flat light | Slightly cooler |
| Fog | Short view distance | Ambush play; less server bandwidth (a smaller relevance radius for some content) |
| Rain | Rain particles, wet surfaces | Wet status → colder; rain barrels fill |
| Storm | Heavy rain, wind, thunder | Cold, noise masks footsteps |
| *Snow, Blizzard (Winter Season)* | See [11](11-seasonal-content-and-winter-update.md) | Strong cold, visibility loss |

## 16. Server rule variants (presets)

| Preset | PvP | Gather | Sleepers | Offline protection | Wipe |
|--------|-----|--------|----------|--------------------|------|
| Official Standard | On | 1× | protected-in-claim | 0.5× after 15 min | Every 2 weeks |
| Official Casual | On, with raid windows | 2× | protected-in-claim | 0.25× | Monthly |
| Community PvE | Off | Any | vanish | n/a | Owner choice |
| Community Hardcore | On | 1× | vulnerable | Off | Owner choice |

## 17. Explicitly out of scope for 1.0 (candidates for later)

Vehicles and boats, electricity/automation, farming and animal husbandry, NPC factions and hostile
humanoid AI, horses, procedurally generated maps, cross-platform play (PC/Android), mod support,
clans larger than a team, player-run shops. The architecture leaves room for each. None of them is in
the 1.0 commitment.
