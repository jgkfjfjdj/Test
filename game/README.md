# game/ — Unity project (iOS client + Linux dedicated server)

This folder is the Unity 6 LTS project. In M0 it contains code, assembly definitions and editor
tooling only. Unity generates `ProjectSettings/`, `.meta` files and `Packages/packages-lock.json`
the first time the project is opened. **Commit those generated files**: they pin the engine version
and package versions for everyone (and for CI).

## Unity version and packages

The project pins **Unity 6000.3.27f2** (`ProjectSettings/ProjectVersion.txt`) and exact package versions
(`Packages/manifest.json`, taken from that editor's bundled manifest). CI builds the project without
anyone opening it first: every CI build applies the project setup (player settings, Boot scene, URP
tiers). The iPhone build pipeline is described in [docs/guides/ios-testflight.md](../docs/guides/ios-testflight.md).

## First open in the editor (optional — for working on the game locally)

1. Install **Unity Hub** and **Unity 6000.3.27f2** with these modules:
   iOS Build Support, Linux Dedicated Server Build Support (IL2CPP), Mac Build Support.
2. Unity Hub → **Add → Add project from disk** → select this `game/` folder (Hub picks 6000.3.27f2 from `ProjectVersion.txt`).
3. If Unity asks to enable the new Input System backends, choose **Yes** (the editor restarts).
4. Menu **Kindling → Setup → 2. Configure Project** (iOS/server player settings, Boot scene).
5. Menu **Kindling → Setup → 3. Create Render Pipeline Tiers** (Low/Medium/High URP assets).
6. **Window → General → Test Runner → EditMode → Run All**. All tests should pass.
7. Press **Play** in the Boot scene: the KINDLING menu appears. **Play offline** starts a local
   server; WASD + mouse to move (click to capture the cursor). See
   [docs/guides/playtest-m1.md](../docs/guides/playtest-m1.md).
8. Optional build checks:
   - **Kindling → Build → Linux Dedicated Server** → `game/Builds/Server/Linux/`
   - **Kindling → Build → iOS Xcode Project** → `game/Builds/iOS/` → open in Xcode → run on your iPhone
     (set your Team and bundle id in Xcode/Player Settings first).
9. Commit what Unity generated (`.meta` files, `ProjectSettings/`, `Packages/packages-lock.json`):
   ```bash
   git add game/ProjectSettings game/Packages game/Assets
   git commit -m "Unity: generated project settings and meta files"
   git push
   ```

If something fails, send me the full Console error text (or `~/Library/Logs/Unity/Editor.log`).

## Layout

| Path | Assembly | Notes |
|------|----------|-------|
| `Assets/Kindling/Runtime/Core` | `Kindling.Core` | Pure C# (no UnityEngine): logging facade, tick clock, world grid/chunks, quantization |
| `Assets/Kindling/Runtime/Content` | `Kindling.Content` | Pure C#: content keys (definitions arrive in M3) |
| `Assets/Kindling/Runtime/Simulation` | `Kindling.Simulation` | Pure C#: collision world, character motor, greybox layout |
| `Assets/Kindling/Runtime/Net` | `Kindling.Net` | Pure C#: protocol, transports (LiteNetLib, in-memory), `ServerHost`, `ClientSession` |
| `Assets/Kindling/Runtime/Controls` | `Kindling.Controls` | Pure C#: touch router, joystick, look, buttons, layout |
| `Assets/Kindling/Runtime/Gameplay` | `Kindling.Gameplay` | Unity runtime: Boot scene bootstrapper, dedicated server runner |
| `Assets/Kindling/Runtime/Client` | `Kindling.Client` | Unity client (not in server builds): menu, HUD, camera, world and player views, input |
| `Assets/ThirdParty/LiteNetLib` | `LiteNetLib` | Vendored UDP library (MIT), see `VERSION.md` |
| `Assets/Kindling/Editor` | `Kindling.Editor` | Setup menu, build entry points |
| `Assets/Kindling/Editor/Rendering` | `Kindling.Editor.Rendering` | URP tier assets (compiles only once URP is installed) |
| `Assets/Kindling/Tests/EditMode` | `Kindling.Tests.EditMode` | NUnit tests; `Pure/` also runs under `dotnet test` (see `sim/`) |

Backend/Platform assemblies are added with their first code (see docs/blueprint/12-folder-structure.md).
Everything is created in code at runtime (greybox, lights, avatars, HUD), so the Boot scene stays the
only scene for now.

**Without Unity:** `dotnet test sim/Kindling.Sim.Tests` runs the pure-C# tests, and
`tools/unity-compile-check/run.sh` compiles the Unity assemblies against the real Unity 6000.3 DLLs.

## Rules
- Pure assemblies (`noEngineReferences: true`) must stay free of UnityEngine so `sim/` can compile them.
- C# 9 only (Unity 6). No `record`, no `init` accessors, no file-scoped namespaces.
- Scenes and prefabs are text (Force Text). Binary art goes through Git LFS (see `.gitattributes`).
