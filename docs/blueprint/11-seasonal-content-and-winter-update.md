# 11 — Seasonal Content System and the Winter Update

## 1. Date reality check

The brief asked for a "Winter 2025" update. Today is **2026-11-07**, so Winter 2025 is in the past, and
the game hasn't been built yet. Our schedule (see [14](14-roadmap.md)):

| Option | When | What |
|--------|------|------|
| Winter event during the open beta | Dec 2027 – Jan 2028 (if M9 finishes on time) | A reduced Winter Season: snow weather, cold mechanics, a few recipes and cosmetics. Proves the season system works |
| **Full Winter Update** | **Winter 2028–29** (first winter after launch) | Everything listed in §4 |

Recommendation: plan the full Winter Update as the **first post-launch season**, and run the reduced
version during the beta if the timing allows.

## 2. The Season framework (built in M9, reusable for every season)

A **Season Pack** is a bundle of data that changes the game for a period of time without new code,
as long as the code it needs is already in the binary.

```
SeasonDef (ScriptableObject, id "season.winter")
├─ schedule              → comes from the backend `seasons` row (dates are never hard-coded)
├─ requiredBuild         → minimum binary build that contains the season's code
├─ addressablesGroup     → "Season_winter" (remote bundles: textures, meshes, VFX, audio)
├─ biomeOverrides[]      → per biome: terrain layer swaps (snow splat), vegetation variants, ambient audio
├─ weatherProfile        → weather state weights + temperature offsets (adds Snow, Blizzard)
├─ survivalModifiers     → e.g. base warmth −15, cold drain ×1.3
├─ itemDefs[]            → new items (resources, clothing, decorations, cosmetics)
├─ recipeDefs[]          → new recipes (+ the station that crafts each)
├─ lootTableOverrides[]  → extra or replacement entries for specific loot tables
├─ resourceSpawns[]      → seasonal nodes (ice formations, frost plants) and the spawn rules
├─ worldEvents[]         → event definitions (e.g. supply sled drops) run by the event system
├─ challenges            → keys that match backend `challenges` rows (stat-based objectives)
├─ cosmetics[]           → entitlement item keys (earned or bought)
└─ sunset rules          → what happens to seasonal items when the season ends
```

**How a season turns on**
1. The binary with the season's code ships ~3 weeks early (code hidden behind
   `season.<key>.enabled = false`).
2. The content release (Addressables) is published. Clients download it in the background.
3. On the start date, the backend sets the season live → bootstrap and the server link announce it →
   official servers apply biome and weather overrides **at the next restart or wipe** (we don't swap
   terrain mid-session). Community servers follow the global season if `seasonal.followGlobalSeason` is
   `true`.
4. Challenges open; official servers report progress; rewards are granted as entitlements.
5. **End of season:** overrides are removed at the next restart. Seasonal *resources* turn into a
   neutral resource (e.g. Ice → Water) or decay. Seasonal *decorations* that were built stay until the
   next wipe. *Cosmetics* are kept forever.

**Rules for future seasons**
- Seasonal items never give lasting gameplay power after the season ends.
- Each season reuses the same systems (weather, biome override, events, challenges). New code is only
  added when a season needs a genuinely new mechanic.
- Template seasons for later: Autumn Harvest, Spring Thaw, a Halloween-style night event.

## 3. Systems the Winter Update depends on (and when they're built)

| System | Built in | Winter extension |
|--------|----------|------------------|
| Warmth / temperature | M3 | Stronger cold, frostbite status, insulation values on clothing |
| Weather state machine | M6 | Snow and Blizzard states; wind direction; visibility fog |
| Biome override (terrain layer swap, vegetation variants) | M9 | Snow-covered map variant |
| Event system (scheduled world events) | M9 | Supply Sled event, Long Night event |
| Challenges and entitlements | M9 | Winter challenges and rewards |
| Addressables remote content | M9 | Winter bundle |

## 4. Winter Update content plan

### 4.1 Environments
- **Snow-covered map variant:** a snow terrain layer blended by slope and height (more snow on flat
  ground and peaks), snow-capped trees (vegetation variant prefabs), frozen waterfalls as static props,
  icicles on points of interest.
- **Frozen lake edges:** shallow lake rims become walkable ice (static colliders swapped by the
  season). Deep water stays open, as a cold-water danger.
- **Ice caves:** two new cave sections, opened by the season (blocked by snow drifts outside winter),
  with new resources and loot.
- Footprints in snow (client-side decals, LOD-limited); breath vapor VFX; muffled audio in snowfall.

### 4.2 Weather and survival
| State | Effect |
|-------|--------|
| Light snow | Warmth −10; snow builds up on surfaces (shader) |
| Heavy snow | Warmth −20; view distance 60%; footsteps quieter |
| **Snowstorm / Blizzard** | Warmth −40; view distance 25%; wind slows movement against it; HUD frost; lasts 3–6 minutes, announced 60 s ahead by sound and sky |
| Clear frost night | Warmth −30; brilliant sky (a visual reward) |

- New status: **Frostbite** (if warmth stays below −60 for 60 s) → reduced max stamina until warmed by
  a fire.
- Clothing **insulation** stat shown on items. A campfire / heater inside a claim gives the "Warm Shelter"
  comfort bonus.
- Performance: blizzard visibility loss lets us **shrink interest radii** for that period (less network
  and render load).

### 4.3 New resources
| Resource | Source | Use |
|----------|--------|-----|
| Ice Block | Mined from ice formations | Melt to water; ice wall decoration; cold storage (slows food spoilage — if food spoilage is in 1.0) |
| Thick Pelt | Winter wolves and stags | Insulated clothing |
| Frostbloom | Rare plant on the snowline | Warming Tonic (temporary cold resistance) |
| Rimesteel Ore | Ice caves (rare) | Seasonal tools and decorations with a distinctive look (equivalent in stats to T2/T3, not stronger) |

### 4.4 New crafting recipes (examples)
Warming Tonic, Insulated Parka/Trousers/Boots/Mittens, Fur Hood, Snowshoes (faster on snow), Ice Pick
(Rimesteel), Heater (burns Lamp Oil; warms a room), Ice Wall (decorative barrier, melts after the season),
Sled (drag-carry tool for heavy props, using the grab system), Lantern of Frost (decorative light).

### 4.5 Seasonal cosmetics
Knit hats and scarves, a winter camo weapon skin set, a frosted building skin (frame trim), a festive
light string emote. Earned through challenges, plus optional cosmetic bundles (IAP).

### 4.6 Seasonal events
| Event | Type | Description |
|-------|------|-------------|
| **Supply Sled** | World event (several times a day) | A sled crashes at a random snowfield; heavy loot crates must be physically hauled away; marked on the map after 2 minutes, which draws PvP |
| **The Long Night** | Timed event (weekend) | Night lasts 3× as long; stronger cold; rarer loot at points of interest; aurora sky |
| **Snowman Contest** | Social/community | Build snowmen from snow piles (grab-and-stack); vote with gestures; an owner tool for community servers |
| **Frozen Raids** | Server preset (opt-in) | Explosives scarcer; fire damage stronger (warm-vs-cold theme) |

### 4.7 Winter loot
Updated loot tables at points of interest: winter clothing, Warming Tonics, Lamp Oil, Rimesteel
fragments, seasonal decorations, cosmetic "frozen trinkets" (unlocks).

### 4.8 New building decorations
Light strings (colored, toggleable), wreath-style door decorations, snow-covered roof variants (skins),
ice sculptures (placeable), fireplace (warmth + comfort), festive table, frosted window panes.

### 4.9 Limited-time challenges (examples)
| Challenge | Objective | Reward |
|-----------|-----------|--------|
| Cold Blooded | Survive 2 blizzards outdoors | Fur-trim hat |
| Sled Hauler | Bring 3 Supply Sled crates into your claim | Sled skin |
| Hearthkeeper | Keep a heater burning in your base for 5 hours total | Ember light string |
| Snow Sculptor | Build 5 snowmen | Snowman emote |
| Long Night Survivor | Play 60 minutes during The Long Night | Aurora banner |

Challenges count on **official servers** only (community servers are untrusted for rewards).

### 4.10 Performance improvements bundled with the update
- Snow shaders tuned per tier (Low: no snow accumulation, only the swapped splat map).
- Weather particles GPU-driven with tier caps; blizzard fog replaces geometry at range (cheaper).
- Re-baked occlusion for the ice caves; impostors for the snow-capped tree variants.
- Optimizations from the season's profiling: building mesh combining, chunk serialization (Burst),
  GC-free UI updates — whatever the post-launch telemetry shows are the worst frame-time costs.

## 5. Winter Update production schedule (template, counted back from the start date S)

| When | Milestone |
|------|-----------|
| S − 12 weeks | Content design locked; asset list to artists |
| S − 8 weeks | Code features done (behind flags); test on staging with the season forced on |
| S − 5 weeks | Binary submitted to App Review (leaves time for one rejection or resubmit) |
| S − 3 weeks | Binary released (phased); Addressables content release published (background download) |
| S − 1 week | Official servers updated; season rehearsal on staging; marketing assets ready |
| S | Season live; events scheduled; challenges open |
| S + 1–2 weeks | Balance patch through remote config / server content |
| End | Sunset at the next restart or wipe; retrospective; telemetry review |
