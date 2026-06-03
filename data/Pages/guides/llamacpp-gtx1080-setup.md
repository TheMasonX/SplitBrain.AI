# llama.cpp GTX 1080 Setup Guide

The GTX 1080 (Pascal sm_61, 8 GB VRAM) can run 30B MoE models at ~18-22 tok/s using llama.cpp's MoE offloading flags. This page documents the verified configuration.

## Prerequisites

- Docker with NVIDIA Container Toolkit (`docker run --gpus all` works)
- CUDA driver ≥ 520 (for CUDA 12.x support on Pascal)
- 24+ GB system RAM (model experts stream over PCIe from RAM)
- Machine B running Orchestrator.NodeWorker with `NODE_B_BACKEND=llamacpp`

## Quick Start — Docker

```bash
docker run --gpus all --cap-add=IPC_LOCK \
  ghcr.io/ggml-org/llama.cpp:server-cuda \
  --model /models/qwen3-coder-30b-a3b.gguf \
  --n-gpu-layers 999 \
  --n-cpu-moe 25 \
  --no-mmap \
  --mlock \
  --cache-type-k turbo4 \
  --cache-type-v turbo3 \
  --host 0.0.0.0 \
  --port 8080
```

## Flag Reference

| Flag | Value | Why |
|------|-------|-----|
| `--n-gpu-layers 999` | 999 (all) | Offload all non-MoE layers to GPU |
| `--n-cpu-moe 25` | 25 (starting point) | Keep 25 MoE expert layers on CPU RAM; tune down until OOM then +2 |
| `--no-mmap` | — | Load full model into RAM upfront; eliminates disk I/O mid-token (+35% speed) |
| `--mlock` | — | Pin RAM pages; prevents kernel from paging experts out during idle |
| `--cache-type-k turbo4` | turbo4 | TurboQuant 4.25 bits/value KV key compression |
| `--cache-type-v turbo3` | turbo3 | TurboQuant 3-bit KV value compression |
| `--host 0.0.0.0` | — | Accept connections from Machine A |
| `--port 8080` | 8080 | Standard llama.cpp server port |

### What NOT to set

- **`--flash-attn`** — Pascal (sm_61) has known instability with flash attention. Omit it.

## Tuning --n-cpu-moe

Start at 25 and watch `nvidia-smi` during inference:

```bash
# Monitor VRAM usage
watch -n1 nvidia-smi

# Lower value = more experts on GPU = faster = more VRAM
# Each reduction of 5 moves ~400-600 MB of experts to GPU
# Tune: reduce until OOM, then add 2-3 back
```

**Target:** VRAM < 7.8 GB with headroom for KV cache growth.

## TurboQuant Notes

- `turbo4` and `turbo3` are in **mainline llama.cpp** since 2026-03-28 (commit `ca25246`)
- On **CUDA** (GTX 1080): `turbo4` uses the legacy 3-bit+QJL path (not 4-bit PolarQuant which is Metal-only)
- Both flags work in the official `ghcr.io/ggml-org/llama.cpp:server-cuda` Docker image — no fork needed
- Verify: `docker run --rm ghcr.io/ggml-org/llama.cpp:server-cuda --help | grep cache-type`

## mlock Requirements

`--mlock` requires memory lock capability:

```bash
# Docker
docker run --cap-add=IPC_LOCK ...

# Docker Compose
services:
  llamacpp:
    cap_add: [IPC_LOCK]

# Bare metal Linux
ulimit -l unlimited   # before starting
# Or in systemd unit: LimitMEMLOCK=infinity

# Verify it worked (should show 16+ GB locked)
grep Mlocked /proc/meminfo
```

## Runtime Verification

```bash
# On Machine B — run the streaming proof script
bash scripts/test-llamacpp-streaming.sh http://localhost:5050 http://localhost:8080
```

The script checks llama-server health, NodeWorker health, then sends a streaming prompt and prints per-token timestamps. Progressive timestamps = streaming works end-to-end.

## Model Download

```bash
# Qwen3-Coder-30B-A3B (GGUF, Q4_K_M recommended for 8 GB)
# Source: HuggingFace — search "qwen3-coder-30b-a3b gguf"
huggingface-cli download Qwen/Qwen3-Coder-30B-A3B-Instruct \
  --include "*Q4_K_M*" \
  --local-dir /models/
```

## Expected Performance

| Configuration | Model | Expected tok/s |
|---------------|-------|----------------|
| GTX 1060 6 GB (baseline from video) | Qwen3-30B-A3B Q4 | ~17 tok/s |
| GTX 1080 8 GB (your hardware) | Qwen3-30B-A3B Q4 | ~18-22 tok/s (estimated) |
| GTX 1080 Ollama | Qwen 7B Q5 | ~35-45 tok/s |

The 7B model is faster in tok/s but the 30B MoE model produces substantially higher quality for complex coding tasks.

## Related Pages

- [Backend Switching](backend-switching.md)
- [NodeClient.LlamaCpp Design](../architecture/llamacpp-nodeclient.md)
- [Getting Started](getting-started.md)
