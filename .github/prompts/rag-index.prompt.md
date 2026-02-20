---
mode: agent
description: "Index repository into NebulaRAG"
---

Index this repository into NebulaRAG.

Steps:
1. Ensure schema is initialized first (run init if needed).
2. Run:
   - `dotnet run --project src\NebulaRAG.Cli -- index --source .`
3. Run a quick verification query:
   - `dotnet run --project src\NebulaRAG.Cli -- query --text "main components in this repo" --limit 3`
4. Return indexed/skipped/chunk counts and whether query returned results.
