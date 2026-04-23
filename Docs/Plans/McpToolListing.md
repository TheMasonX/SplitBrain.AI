A couple of ground rules I enforced while designing these:

* All tools are **stateless at the boundary** (state lives in your agent engine)
* Every response is **machine-parseable first, human-readable second**
* All tools support **partial/streamed results**, even if not shown in schema
* Errors are **structured**, not strings

---

# 15. MCP Tool Schemas (Canonical Contracts)

---

## 15.1 Shared Types (Used Across All Tools)

### Error Object

```json
{
  "error": {
    "code": "string",
    "message": "string",
    "retryable": true,
    "details": {}
  }
}
```

---

### Metadata Envelope

```json
{
  "meta": {
    "taskId": "string",
    "node": "A | B",
    "model": "string",
    "latencyMs": 0,
    "tokensIn": 0,
    "tokensOut": 0
  }
}
```

---

### Diff Format (Unified)

```json
{
  "diff": {
    "files": [
      {
        "path": "string",
        "changeType": "modify | create | delete",
        "patch": "unified diff string"
      }
    ]
  }
}
```

---

# 15.2 `review_code`

## Purpose

Deep architectural or code-quality review.

---

### Request

```json
{
  "code": "string",
  "language": "string",
  "focus": "architecture | performance | bugs | readability | security",
  "context": {
    "relatedFiles": [
      {
        "path": "string",
        "content": "string"
      }
    ]
  }
}
```

---

### Response

```json
{
  "summary": "string",
  "issues": [
    {
      "severity": "low | medium | high | critical",
      "type": "bug | design | performance | style",
      "message": "string",
      "location": {
        "file": "string",
        "lineStart": 0,
        "lineEnd": 0
      },
      "suggestion": "string"
    }
  ],
  "suggestedDiff": {
    "files": []
  },
  "meta": {}
}
```

---

# 15.3 `refactor_code`

## Purpose

Multi-file or structural improvements.

---

### Request

```json
{
  "goal": "string",
  "codebase": [
    {
      "path": "string",
      "content": "string"
    }
  ],
  "constraints": {
    "preserveBehavior": true,
    "maxFiles": 10
  }
}
```

---

### Response

```json
{
  "summary": "string",
  "changes": {
    "files": []
  },
  "riskLevel": "low | medium | high",
  "meta": {}
}
```

---

# 15.4 `generate_tests`

## Purpose

Create unit/integration tests.

---

### Request

```json
{
  "code": "string",
  "language": "string",
  "framework": "xunit | nunit | jest | playwright",
  "coverageTarget": "basic | edge | exhaustive"
}
```

---

### Response

```json
{
  "tests": [
    {
      "path": "string",
      "content": "string"
    }
  ],
  "coverageEstimate": 0.0,
  "notes": "string",
  "meta": {}
}
```

---

# 15.5 `run_tests`

## Purpose

Execute tests and return structured results.

---

### Request

```json
{
  "projectPath": "string",
  "testFilter": "string",
  "timeoutSeconds": 30
}
```

---

### Response

```json
{
  "summary": {
    "total": 0,
    "passed": 0,
    "failed": 0,
    "skipped": 0,
    "durationMs": 0
  },
  "failures": [
    {
      "testName": "string",
      "message": "string",
      "stackTrace": "string"
    }
  ],
  "logs": "string",
  "meta": {}
}
```

---

# 15.6 `apply_patch`

## Purpose

Apply diff safely to filesystem.

---

### Request

```json
{
  "diff": {
    "files": []
  },
  "dryRun": false
}
```

---

### Response

```json
{
  "applied": true,
  "filesChanged": [
    "string"
  ],
  "conflicts": [
    {
      "path": "string",
      "reason": "string"
    }
  ],
  "meta": {}
}
```

---

# 15.7 `search_codebase`

## Purpose

Semantic + keyword search.

---

### Request

```json
{
  "query": "string",
  "topK": 5,
  "filters": {
    "path": "string",
    "language": "string"
  }
}
```

---

### Response

```json
{
  "results": [
    {
      "path": "string",
      "snippet": "string",
      "score": 0.0
    }
  ],
  "meta": {}
}
```

---

# 15.8 `query_ui`

## Purpose

Expose UI state to AI (Playwright/FlaUI abstraction)

---

### Request

```json
{
  "target": "web | desktop",
  "action": "snapshot | find | interact",
  "selector": "string"
}
```

---

### Response

```json
{
  "screen": "string",
  "elements": [
    {
      "type": "button | input | label | checkbox",
      "label": "string",
      "value": "string",
      "visible": true,
      "enabled": true
    }
  ],
  "meta": {}
}
```

---

# 15.9 `run_ui_test`

## Purpose

Execute UI automation scenario

---

### Request

```json
{
  "steps": [
    {
      "action": "click | type | navigate | assert",
      "target": "string",
      "value": "string"
    }
  ],
  "timeoutSeconds": 30
}
```

---

### Response

```json
{
  "success": true,
  "steps": [
    {
      "action": "string",
      "result": "ok | failed",
      "details": "string"
    }
  ],
  "screenshot": "base64",
  "meta": {}
}
```

---

# 15.10 `analyze_project`

## Purpose

High-level architecture understanding

---

### Request

```json
{
  "files": [
    {
      "path": "string",
      "content": "string"
    }
  ]
}
```

---

### Response

```json
{
  "summary": "string",
  "modules": [
    {
      "name": "string",
      "responsibility": "string",
      "dependencies": ["string"]
    }
  ],
  "risks": [
    "string"
  ],
  "meta": {}
}
```

---

# 15.11 Design Notes (Important)

## Determinism

* All tools must prefer:

  * structured outputs
  * stable formats
* Avoid freeform text unless in `"summary"`

---

## Streaming

Even though schemas are static:

* `summary`
* `issues`
* `diff`

…should be streamable in chunks

---

## Versioning (Add Immediately)

Every request should include:

```json
{
  "version": "1.0"
}
```

This saves you later when you evolve formats.
