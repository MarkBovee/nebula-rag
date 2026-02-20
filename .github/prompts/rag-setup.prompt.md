---
mode: agent
description: "Run full NebulaRAG setup"
---

Set up NebulaRAG end-to-end for this repository.

Steps:
1. Verify `src\NebulaRAG.Cli\ragsettings.json` exists.
2. Verify `src\NebulaRAG.Cli\ragsettings.local.json` exists and has a non-placeholder password. If placeholder is still present, ask the user for the password and update only that field.
3. Run schema init:
   - `dotnet run --project src\NebulaRAG.Cli -- init`
4. Run indexing for the current repository:
   - `dotnet run --project src\NebulaRAG.Cli -- index --source .`
5. Run a smoke query:
   - `dotnet run --project src\NebulaRAG.Cli -- query --text "what is this project about?" --limit 3`
6. Report concise status (init/index/query) and next action if something failed.

Rules:
- Keep edits minimal.
- Do not modify unrelated files.
- Never print secrets in output.
