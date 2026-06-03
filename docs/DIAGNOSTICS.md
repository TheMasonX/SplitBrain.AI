# Diagnostics Guide

## Broken Tool Timeout Investigation (P0.1-P0.3)

Three tools timeout at 4 minutes: `refactor_code`, `generate_tests`, `agent_task`.

### Step 1: Check raw response log
Run the tool. Check `%TEMP%/splitbrain-mcp-*.log` for a line containing `[RefactorCodeTool] Raw response`.
- If present: the model responded; the bug is in parsing. Check the content.
- If absent: the model is hanging. Proceed to Step 2.

### Step 2: Test Ollama directly
Use the `.http` files in `tests/http/`:
- `tests/http/refactor_ollama.http`
- `tests/http/generate_tests_ollama.http`

Send via VS Code REST Client, IntelliJ HTTP client, or curl:
```bash
curl -X POST http://localhost:11434/api/generate \
  -H "Content-Type: application/json" \
  -d @tests/http/refactor_ollama.http
```

### Step 3: Likely fixes
- If model returns markdown fences: `ResponseParser.StripFences()` is now in the pipeline
- If model returns plain text: check `format: json` is being sent in OllamaGenerateRequest
- If model hangs: try a different model (qwen2.5-coder:7b-instruct for generation tasks)
