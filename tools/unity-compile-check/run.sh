#!/usr/bin/env bash
# Compile-checks the Unity C# (client, server, editor scripts) against the real Unity managed assemblies,
# extracted from GameCI's public editor images, and the Input System package source. No Unity license
# or editor install needed; nothing is executed. See README.md.
#
# Usage: tools/unity-compile-check/run.sh [player|server|editor|ios-editor|all]   (default: all)
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
MODE="${1:-all}"
CACHE="${UNITY_CHECK_CACHE:-$HOME/.cache/kindling-unity-check}"
UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' "$REPO/game/ProjectSettings/ProjectVersion.txt")"
INPUT_VERSION="$(python3 -c "import json;print(json.load(open('$REPO/game/Packages/manifest.json'))['dependencies']['com.unity.inputsystem'])")"
DATA="$CACHE/unity-$UNITY_VERSION/opt/unity/Editor/Data"
mkdir -p "$CACHE"

registry_token() {
  curl -fsS "https://auth.docker.io/token?service=registry.docker.io&scope=repository:unityci/editor:pull" | python3 -c "import sys,json;print(json.load(sys.stdin)['token'])"
}

# Streams the biggest layer of an image tag (the editor) and extracts only the given paths.
extract_from_image() {
  local tag="$1"; shift
  local token index digest layer
  token="$(registry_token)"
  index="$(curl -fsS -H "Authorization: Bearer $token" -H "Accept: application/vnd.oci.image.index.v1+json, application/vnd.docker.distribution.manifest.list.v2+json" \
    "https://registry-1.docker.io/v2/unityci/editor/manifests/$tag")"
  digest="$(echo "$index" | python3 -c "import sys,json;d=json.load(sys.stdin);print(next(m['digest'] for m in d['manifests'] if m.get('platform',{}).get('architecture')=='amd64'))")"
  layer="$(curl -fsS -H "Authorization: Bearer $token" -H "Accept: application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.v2+json" \
    "https://registry-1.docker.io/v2/unityci/editor/manifests/$digest" | python3 -c "import sys,json;d=json.load(sys.stdin);print(max(d['layers'],key=lambda l:l['size'])['digest'])")"
  echo "Streaming $tag layer $layer (several GB; only the needed files are kept)…"
  mkdir -p "$CACHE/unity-$UNITY_VERSION"
  # tar exits non-zero with SIGPIPE-like errors if we stop early; we always read to the end.
  curl -fsSL -H "Authorization: Bearer $token" "https://registry-1.docker.io/v2/unityci/editor/blobs/$layer" \
    | tar -xz -C "$CACHE/unity-$UNITY_VERSION" --wildcards --no-anchored "$@"
}

if [[ ! -f "$DATA/Managed/UnityEngine/UnityEngine.CoreModule.dll" ]]; then
  extract_from_image "ubuntu-$UNITY_VERSION-base-3" '*/Editor/Data/Managed/UnityEngine/*.dll'
fi
if [[ "$MODE" == "ios-editor" || "$MODE" == "all" ]] && [[ ! -f "$DATA/PlaybackEngines/iOSSupport/UnityEditor.iOS.Extensions.Xcode.dll" ]]; then
  extract_from_image "ubuntu-$UNITY_VERSION-ios-3" '*/PlaybackEngines/iOSSupport/UnityEditor.iOS.Extensions*.dll'
fi

INPUT_DIR="$CACHE/inputsystem-$INPUT_VERSION"
if [[ ! -d "$INPUT_DIR" ]]; then
  GIT_LFS_SKIP_SMUDGE=1 git clone -q --depth 1 --branch "$INPUT_VERSION" https://github.com/Unity-Technologies/InputSystem "$INPUT_DIR"
fi

OUT="$CACHE/projects"
modes=("$MODE"); [[ "$MODE" == "all" ]] && modes=(player server editor ios-editor)
status=0
for m in "${modes[@]}"; do
  python3 "$HERE/gen.py" "$DATA" "$INPUT_DIR/Packages/com.unity.inputsystem/InputSystem" "$OUT" "$m"
  case "$m" in
    player) target=Kindling.Client ;;
    server) target=Kindling.Gameplay ;;
    *) target=Kindling.Editor ;;
  esac
  echo "== $m: $target"
  if ! dotnet build "$OUT/$m/$target/$target.csproj" -c Release -nologo -v quiet 2>&1 | grep -E "error|warning CS|Build succeeded" | grep -v "/inputsystem-" | sed "s|$REPO/||; s| \[.*||" | sort -u; then
    status=1
  fi
  dotnet build "$OUT/$m/$target/$target.csproj" -c Release -nologo -v quiet >/dev/null 2>&1 || status=1
done
exit $status
