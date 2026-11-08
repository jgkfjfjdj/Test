# ADR 0007 — Netcode: LiteNetLib transport + our own engine-independent simulation

- **Status:** Accepted (2026-11-08). **Supersedes ADR 0002** (FishNet), as allowed by its M1 decision gate.

## Context
ADR 0002 chose FishNet but already put the genre-critical parts in our own code (chunk replication,
lag compensation, join tickets, resume tokens, server link). Starting M1 showed two more facts:

1. FishNet's prediction, replication and observers can only be compiled, run and tested inside Unity.
   The project has no Unity license in CI yet and the developer may not have a Mac, so FishNet-based
   netcode could not be tested at all until much later.
2. For a 50-player survival game with tens of thousands of static entities, the data-oriented design
   (blueprint 05) uses little of FishNet's object model (NetworkObjects, SyncVars, RPC codegen).

## Decision
- **Transport:** LiteNetLib (MIT, v1.2.0, vendored source) — reliable/unreliable UDP, IPv6 dual-stack,
  pure C#, runs in Unity (IL2CPP) and plain .NET. (FishNet's own UDP transport, Tugboat, is LiteNetLib.)
- **Simulation and netcode are engine-independent C#** (`Kindling.Simulation`, `Kindling.Net`,
  `Kindling.Controls`, all `noEngineReferences`):
  - Movement uses our own deterministic AABB collision world built from map data, not PhysX, so the
    iPhone client, the dedicated server and bots run the *same* code (no prediction drift from engine
    differences).
  - Server-authoritative, sequence-numbered inputs (one input consumed per tick per player — input
    flooding can't speed a player up), client-side prediction with reconciliation, snapshot
    interpolation for other players, interest management, resume tokens.
  - The same code runs in a .NET **dev server** and **bot swarm**, so netcode is tested over real UDP
    in this repository's CI, without Unity.
- **Unity's role:** rendering, input devices, audio, UI, and hosting the same server core in the
  dedicated server build (later: NavMesh, animation).

## Consequences
- More code owned by us (player replication, interest management, clock handling) — offset by
  testability: protocol, prediction and load can be tested with plain `dotnet test`.
- Movement collision is limited to what our collision world supports (boxes now; heightfield terrain
  in M6). Steps replace slopes on the greybox map.
- Float determinism across platforms (ARM64 iPhone vs x64 server) is "close", not bit-exact;
  reconciliation corrects small differences (threshold 1 cm).
- FishNet is not added to the project.
