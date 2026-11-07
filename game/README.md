# game/ — Unity project (iOS client + Linux dedicated server)

This folder is the Unity 6 LTS project. In M0 it contains code, assembly definitions and editor
tooling only. Unity generates `ProjectSettings/`, `.meta` files and `Packages/packages-lock.json`
the first time the project is opened. **Commit those generated files**: they pin the engine version
and package versions for everyone (and for CI).

## First open (one time, on your Mac)

1. Install **Unity Hub** and the newest **Unity 6 LTS** editor with these modules:
   iOS Build Support, Linux Dedicated Server Build Support (IL2CPP), Mac Build Support.
2. Unity Hub → **Add → Add project from disk** → select this `game/` folder → open it with Unity 6 LTS.
   (Hub may say the version is unknown — choose your Unity 6 LTS editor.)
3. A dialog **"KINDLING project setup"** lists missing packages → **Install**.
   If Unity asks to enable the new Input System backends, choose **Yes** (the editor restarts).
4. Menu **Kindling → Setup → 2. Configure Project** (iOS/server player settings, Boot scene).
5. Menu **Kindling → Setup → 3. Create Render Pipeline Tiers** (Low/Medium/High URP assets).
6. **Window → General → Test Runner → EditMode → Run All**. All tests should pass.
7. Press **Play** in the Boot scene. The Console shows `[Boot] KINDLING client …`.
8. Optional build checks:
   - **Kindling → Build → Linux Dedicated Server** → `game/Builds/Server/Linux/`
   - **Kindling → Build → iOS Xcode Project** → `game/Builds/iOS/` → open in Xcode → run on your iPhone
     (set your Team and bundle id in Xcode/Player Settings first).
9. Commit the generated files:
   ```bash
   git add game/ProjectSettings game/Packages game/Assets
   git commit -m "Unity: initial project settings and package lock"
   git push
   ```
   Then tell me which Unity version you used (it's in `ProjectSettings/ProjectVersion.txt`).

If something fails, send me the full Console error text (or `~/Library/Logs/Unity/Editor.log`).

## Layout

| Path | Assembly | Notes |
|------|----------|-------|
| `Assets/Kindling/Runtime/Core` | `Kindling.Core` | Pure C# (no UnityEngine): logging facade, tick clock, world grid/chunks, quantization |
| `Assets/Kindling/Runtime/Content` | `Kindling.Content` | Pure C#: content keys (definitions arrive in M3) |
| `Assets/Kindling/Runtime/Gameplay` | `Kindling.Gameplay` | Unity runtime: Boot scene bootstrapper |
| `Assets/Kindling/Editor` | `Kindling.Editor` | Setup menu, build entry points |
| `Assets/Kindling/Editor/Rendering` | `Kindling.Editor.Rendering` | URP tier assets (compiles only once URP is installed) |
| `Assets/Kindling/Tests/EditMode` | `Kindling.Tests.EditMode` | NUnit tests; `Pure/` also runs under `dotnet test` (see `sim/`) |

Server/Client/Net/Backend/Platform/Simulation assemblies are added in M1–M3, together with their
first code (see docs/blueprint/12-folder-structure.md).

## Rules
- Pure assemblies (`noEngineReferences: true`) must stay free of UnityEngine so `sim/` can compile them.
- C# 9 only (Unity 6). No `record`, no `init` accessors, no file-scoped namespaces.
- Scenes and prefabs are text (Force Text). Binary art goes through Git LFS (see `.gitattributes`).
