#!/usr/bin/env bash
# Real-UDP load test on one machine: dev server (SQLite world, saves every 10 s) + N bots (half wander,
# half gather/craft/eat), with optional simulated latency/loss.
# Usage: sim/loadtest.sh <bots> <seconds> [latency a-b] [loss %] [output dir]
# Example: sim/loadtest.sh 50 60 30-60 2 /tmp/load
set -euo pipefail
cd "$(dirname "$0")"

BOTS="${1:-30}"
SECONDS_TO_RUN="${2:-60}"
LATENCY="${3:-}"
LOSS="${4:-}"
OUT="${5:-./loadtest-results}"
PORT="${PORT:-27015}"
mkdir -p "$OUT"

SIM_ARGS=()
BUILD_ARGS=(-c Release --no-incremental) # a define change alone would not trigger a rebuild
if [[ -n "$LATENCY" || -n "$LOSS" ]]; then
  BUILD_ARGS+=(-p:LiteNetSimulation=true)
  [[ -n "$LATENCY" ]] && SIM_ARGS+=(--sim-latency "$LATENCY")
  [[ -n "$LOSS" ]] && SIM_ARGS+=(--sim-loss "$LOSS")
fi

dotnet build Kindling.DevServer "${BUILD_ARGS[@]}" -v quiet -nologo >/dev/null
dotnet build Kindling.Bots "${BUILD_ARGS[@]}" -v quiet -nologo >/dev/null

NAME="bots${BOTS}${LATENCY:+-lat$LATENCY}${LOSS:+-loss$LOSS}"
SERVER_SECONDS=$((SECONDS_TO_RUN + BOTS / 10 + 10))
dotnet Kindling.DevServer/bin/Release/net8.0/Kindling.DevServer.dll --port "$PORT" --max-players $((BOTS + 10)) \
  --duration "$SERVER_SECONDS" --stats-interval 5 --stats-json "$OUT/$NAME-server.json" "${SIM_ARGS[@]}" \
  --world "$OUT/$NAME-world.db" --wipe --save-interval 10 \
  > "$OUT/$NAME-server.log" 2>&1 &
SERVER_PID=$!
sleep 2

dotnet Kindling.Bots/bin/Release/net8.0/Kindling.Bots.dll --port "$PORT" --count "$BOTS" \
  --duration "$SECONDS_TO_RUN" --summary-json "$OUT/$NAME-bots.json" "${SIM_ARGS[@]}" \
  > "$OUT/$NAME-bots.log" 2>&1 || true

wait "$SERVER_PID" || true
echo "== $NAME"
grep '\[stats\]' "$OUT/$NAME-server.log" | tail -4
cat "$OUT/$NAME-bots.json"
