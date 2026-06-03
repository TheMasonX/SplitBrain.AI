# NodeCInferenceNode.cs Base64 Corruption

## What Happened
`src/NodeClient.Copilot/NodeCInferenceNode.cs` was stored as base64-encoded
text instead of plain C# source. The file began with a `dXNpbmcg...` base64
string and was ~11KB of encoded data.

## How It Got That Way (Suspected)
Likely a tool or extension encoded the file for transport and wrote the
encoded form back to disk. The exact trigger is unknown.

## How to Fix
Use `certutil` to decode the base64 content back to plain text:
```powershell
certutil -f -decode <InputFile> <TempFile>
Copy-Item -LiteralPath <TempFile> -Destination <InputFile> -Force
```

Then verify the file starts with valid C# (e.g., `using ...;`).

## After Decode — List Initializers
The decoded file used C# collection expressions `[_model]` and
`[new ModelInfo{...}]` which caused CS1733/CS1002 with the current
compiler version. These were replaced with `new List<T>{...}` syntax.

## Prevention
If using encoding for transport, always write the decoded output, not the
encoded input. Validate with `dotnet build` after writing.
