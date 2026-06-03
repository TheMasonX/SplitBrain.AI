# nomic-embed-text — Ollama vs ONNX Clarification

SplitBrain.AI uses `nomic-embed-text` **via Ollama** for embeddings. Pull it with:

```bash
ollama pull nomic-embed-text
```

## What SplitBrain.AI Does NOT Use

SplitBrain.AI does **not** reference `nomic-embed-text-v1.5.onnx` (the ONNX binary file). This filename appears in:

- **MemorySmith** (`Scripts/Install-CodeSearchModel.ps1`) — MemorySmith downloads the ONNX model directly for its offline semantic code-search engine
- **This `data/` directory** — the MemorySmith wiki we added to SplitBrain.AI contains research pages that reference the ONNX file name in documentation context

The SplitBrain.AI installer and deploy scripts pull Ollama models only. No ONNX files are downloaded by the installer.

## Why the Confusion Exists

The `data/` directory in SplitBrain.AI's repo is a MemorySmith wiki. Some pages reference MemorySmith-specific tooling (like `Install-CodeSearchModel.ps1`). These are documentation pages, not scripts that SplitBrain.AI executes.
