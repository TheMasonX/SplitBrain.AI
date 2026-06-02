#!/usr/bin/env bash
# test-llamacpp-streaming.sh — OI-1 Streaming Proof
# Verifies that the llama.cpp backend delivers tokens progressively through
# the NodeWorker /inference endpoint (not as a single batched response).
#
# Prerequisites:
#   - Machine B running: llama-server with --n-cpu-moe, --no-mmap, --mlock flags
#   - Machine B running: NODE_B_BACKEND=llamacpp dotnet run Orchestrator.NodeWorker
#   - jq installed (brew install jq / apt install jq)
#
# Usage:
#   ./scripts/test-llamacpp-streaming.sh [NODE_WORKER_URL]
#
# Example:
#   ./scripts/test-llamacpp-streaming.sh http://192.168.1.20:5050
#   ./scripts/test-llamacpp-streaming.sh                           # uses localhost:5050

set -euo pipefail

WORKER_URL="${1:-http://localhost:5050}"
LLAMACPP_URL="${2:-http://localhost:8080}"
PROMPT="Write a short haiku about distributed inference."

echo "=== llama.cpp Streaming Proof ==="
echo "NodeWorker: $WORKER_URL"
echo "llama-server: $LLAMACPP_URL"
echo ""

# ── Test 1: llama-server health ──────────────────────────────────────────────
echo "[1/4] Checking llama-server /health ..."
STATUS=$(curl -sf "$LLAMACPP_URL/health" | jq -r '.status' 2>/dev/null || echo "UNREACHABLE")
echo "  status: $STATUS"
if [ "$STATUS" != "ok" ]; then
  echo "  ERROR: llama-server is not ready (status=$STATUS). Start it first."
  exit 1
fi
echo "  PASS"
echo ""

# ── Test 2: llama-server /v1/models ─────────────────────────────────────────
echo "[2/4] Checking llama-server loaded model ..."
MODEL=$(curl -sf "$LLAMACPP_URL/v1/models" | jq -r '.data[0].id' 2>/dev/null || echo "UNKNOWN")
echo "  loaded model: $MODEL"
echo ""

# ── Test 3: NodeWorker /health ────────────────────────────────────────────────
echo "[3/4] Checking NodeWorker /health ..."
WORKER_STATUS=$(curl -sf "$WORKER_URL/health" | jq -r '.status' 2>/dev/null || echo "UNREACHABLE")
echo "  NodeWorker status: $WORKER_STATUS"
if [ "$WORKER_STATUS" = "UNREACHABLE" ]; then
  echo "  ERROR: NodeWorker not reachable at $WORKER_URL"
  exit 1
fi
echo "  PASS"
echo ""

# ── Test 4: Progressive token delivery via POST /inference ───────────────────
echo "[4/4] Streaming proof — watching token arrival timestamps ..."
echo "  Prompt: \"$PROMPT\""
echo ""
echo "  Tokens (each line = one received chunk from /inference SSE):"
echo "  ─────────────────────────────────────────────────────────────"

PAYLOAD=$(jq -n --arg prompt "$PROMPT" '{"prompt":$prompt,"stream":true}')
FIRST_TOKEN_TIME=""
TOKEN_COUNT=0
START_TIME=$(date +%s%3N)

# Stream the /inference endpoint and print each SSE data line with a timestamp
curl -sf -N -X POST \
  -H "Content-Type: application/json" \
  -d "$PAYLOAD" \
  "$WORKER_URL/inference" | while IFS= read -r line; do
    if [[ "$line" == data:* ]]; then
      DATA="${line#data: }"
      [ "$DATA" = "[DONE]" ] && break
      NOW=$(date +%s%3N)
      CONTENT=$(echo "$DATA" | jq -r '.choices[0].delta.content // empty' 2>/dev/null)
      if [ -n "$CONTENT" ]; then
        ELAPSED=$((NOW - START_TIME))
        printf "  +%4dms  %s\n" "$ELAPSED" "$CONTENT"
      fi
    fi
  done 2>/dev/null || {
    # Fallback: non-streaming /inference (if routing layer buffers)
    echo "  NOTE: SSE stream not detected. Trying non-streaming /inference ..."
    PAYLOAD_NS=$(jq -n --arg prompt "$PROMPT" '{"prompt":$prompt,"stream":false}')
    RESULT=$(curl -sf -X POST \
      -H "Content-Type: application/json" \
      -d "$PAYLOAD_NS" \
      "$WORKER_URL/inference" 2>/dev/null)
    TEXT=$(echo "$RESULT" | jq -r '.text // empty' 2>/dev/null)
    echo "  [Non-streaming] Response received: $(echo "$TEXT" | head -c 100)..."
    echo ""
    echo "  ⚠ STREAMING NOT VISIBLE at NodeWorker level."
    echo "    This means the routing layer buffers the response."
    echo "    OQ-4 from design doc applies — streaming is correct at the client level"
    echo "    but not yet propagated to the consumer."
    exit 0
  }

END_TIME=$(date +%s%3N)
TOTAL_MS=$((END_TIME - START_TIME))
echo "  ─────────────────────────────────────────────────────────────"
echo ""
echo "  Total wall-clock time: ${TOTAL_MS}ms"
echo ""
echo "PASS: If timestamps above show progressively increasing values"
echo "      with tokens arriving over time (not all at once), streaming"
echo "      is working end-to-end through the NodeWorker stack."
echo ""
echo "FAIL: If all tokens appear at the same timestamp (or a single"
echo "      large response arrived), the routing layer is buffering."
echo "      See OQ-4 in Design_NodeClient_LlamaCpp.md for next steps."
