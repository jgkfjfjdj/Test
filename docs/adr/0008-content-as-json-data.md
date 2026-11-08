# ADR 0008 — Game content as JSON data with permanent network ids

- **Status:** Accepted (2026-11-08, M3). Changes the content-pipeline plan in blueprint 14 (M3).

## Context
The blueprint planned content (items, recipes, loot, …) as Unity ScriptableObjects with an exporter.
ScriptableObjects can only be created, read and validated inside the Unity editor. This project has
no Unity license in CI yet, the server core runs outside Unity (ADR 0007), and the backend and web
console need the same catalogue.

## Decision
- Content is **hand-editable JSON** in `game/Assets/Kindling/Resources/Content/` (`items.json`,
  `recipes.json`, `nodes.json`, `entities.json`, `loot.json`, `rules.json`). Comments are allowed.
  Unity loads them as TextAssets; the .NET server, bots and tests read the files directly.
- `Kindling.Content` parses, resolves references and **validates** every load (missing references,
  wrong kinds, bad numbers, unknown rule settings, duplicate keys…), listing all problems at once.
- **Permanent network ids** live in `content.lock.json`, managed by `sim/Kindling.ContentTool`
  (`update-lock`). Ids are never reused: removed keys are retired, re-added keys get their old id.
  Saves and the wire protocol store ids, so they stay valid across content changes.
- `packages/content/content-export.json` is generated for the backend and web console.
- CI runs `ContentTool check`: content valid, lock file and export up to date.
- A content **fingerprint** (hash of the export) is sent in Hello; mismatched clients are refused
  (`ContentMismatch`) instead of misreading ids.

## Consequences
- Content changes are reviewable diffs and testable with `dotnet test`.
- No editor UI for content yet; designers edit JSON. A ScriptableObject or web editor can be added
  later on top of the same files.
- Rules binding uses reflection over public fields (IL2CPP keeps them since gameplay code reads them);
  watch for stripping if new rule classes are added.
