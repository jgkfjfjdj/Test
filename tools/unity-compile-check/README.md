# Unity compile check (no Unity license needed)

Compiles the Unity-side C# — `Kindling.Client`, `Kindling.Gameplay`, the editor scripts and the
vendored LiteNetLib in its Unity configuration — with `dotnet build`, against:

- the real **Unity managed assemblies** for the pinned editor version (`UnityEngine.*Module.dll`,
  `UnityEditor.*Module.dll`, the iOS Xcode extension), extracted from GameCI's public
  `unityci/editor` images on Docker Hub, and
- the **Input System package source** at the version pinned in `game/Packages/manifest.json`.

Each asmdef becomes its own project with the same references and define constraints, so it also
catches access to another assembly's `internal` members (which `sim/` can't: it compiles all pure
code into one assembly).

```bash
tools/unity-compile-check/run.sh            # player (iOS), server (Linux), editor, ios-editor
tools/unity-compile-check/run.sh player     # just one
```

The first run streams two image layers (~5 GB and ~5.6 GB) and keeps ~100 MB in
`~/.cache/kindling-unity-check` (`UNITY_CHECK_CACHE` to change). Nothing is executed and nothing
from Unity is committed to this repository.

**What it does not check:** URP-dependent editor code (`Kindling.Editor.Rendering`), the Input
System's own editor code, shader/asset/scene problems, IL2CPP code generation, and runtime behavior.
The real gate stays `ci-unity` (GameCI with a Unity license), which runs tests and builds players.
