# llama.cpp TurboQuant — Research Notes

## What is TurboQuant?

TurboQuant is a KV-cache quantization scheme for llama.cpp that dramatically reduces the VRAM footprint of the KV cache, enabling larger context windows on low-VRAM GPUs.

- **turbo3**: 2-bit PolarQuant + 1-bit QJL signs → 3.8× compression vs fp16
- **turbo4**: redesigned in 2026 to use 4-bit PolarQuant (16 optimal centroids)

## Mainline Status

**TurboQuant merged into mainline llama.cpp on 2026-03-28** (commit `ca2524617bb1a7b4c9e00be02d2f37afe31a214e`, author: TheTom).

- `TURBO4_USE_4BIT = 1` on Metal (Apple Silicon) → 4-bit PolarQuant (validated)
- `TURBO4_USE_4BIT = 0` on CUDA → legacy 3-bit+QJL path (until CUDA port is done)

**No fork required.** The official Docker image `ghcr.io/ggml-org/llama.cpp:server-cuda` includes both flags.

## Block Format

Both formats produce **68 bytes per 128 values = 4.25 bits/value** (3.8× compression vs fp16):

```
Block structure (turbo4, CUDA legacy — 3-bit+QJL):
  norm (fp16)           2 bytes
  rnorm (fp16)          2 bytes   ← residual norm for QJL scale
  qs[48] (3-bit indices) 48 bytes
  signs[16] (1-bit QJL) 16 bytes
  ─────────────────────────────
  Total:                68 bytes per 128 values

Block structure (turbo4, Metal — 4-bit PolarQuant):
  norm (fp16)           2 bytes
  rnorm (fp16)          2 bytes   ← reserved
  qs[64] (4-bit nibbles) 64 bytes
  ─────────────────────────────
  Total:                68 bytes per 128 values
```

## Quality Benchmarks (from commit, M5 Max, Qwen3.5-35B-A3B)

| Format | PPL vs q8_0 | Decode |
|--------|------------|--------|
| turbo4 | +0.23% | 79.87 tok/s (0.93×) |
| turbo3 | +1.06% | 76.84 tok/s (0.90×) |

Minimal quality loss vs q8_0, significant memory savings.

## How to Verify

```bash
docker run --rm ghcr.io/ggml-org/llama.cpp:server-cuda --help | grep cache-type
# Should show: --cache-type-k, --cache-type-v options with turbo4/turbo3 as valid values
```

## Context Length Impact

On GTX 1060 6 GB (video baseline):
- No TurboQuant: ~64K context at 17 tok/s
- With `--cache-type-k turbo4 --cache-type-v turbo3`: 256K context at same speed

On GTX 1080 8 GB (estimated):
- Larger base VRAM allows more expert layers on GPU (`--n-cpu-moe 25` vs 35)
- TurboQuant further extends context headroom
- Target: 128K+ context at ~18-22 tok/s for 30B MoE model

## Related Pages

- [GTX 1080 Setup](../guides/llamacpp-gtx1080-setup.md)
- [NodeClient.LlamaCpp Design](../architecture/llamacpp-nodeclient.md)
