# ADR 0001 — Unity 6 LTS as the game engine

- **Status:** Accepted (2026-11-07)
- **Details:** docs/blueprint/02-technology-stack.md §2

## Context
We need a 3D first-person multiplayer game on iPhones from the A13 up, a headless Linux dedicated
server that community members can host, fast iteration for a very small team, and a large licensed
asset ecosystem.

## Decision
Use **Unity 6 LTS** with URP, IL2CPP (iOS client and Linux server), the Input System, Addressables
and Burst/Jobs for hot paths. One Unity project builds both the iOS client and the Dedicated Server
target. The exact LTS patch is pinned by the committed `game/ProjectSettings/ProjectVersion.txt`;
upgrades happen only between milestones, in their own PR, with a profiling comparison.

Engine-independent rules live in assemblies with `noEngineReferences: true` and are also compiled
and tested by `sim/` with `dotnet test` (C# 9, netstandard2.1), so most logic is tested in CI without
a Unity license.

## Consequences
- iOS builds require macOS + Xcode (local Mac or macOS CI).
- Licensing: Unity Personal until US$200k revenue/funding in 12 months, then Pro.
- Unity CI (GameCI) needs a license secret in the repository.
- Unreal and Godot are not used; revisiting would mean a rewrite of client and server code.
