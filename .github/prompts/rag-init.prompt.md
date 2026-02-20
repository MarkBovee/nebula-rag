---
mode: agent
description: "Initialize NebulaRAG database"
---

Initialize NebulaRAG for this repository.

Steps:
1. Validate config files:
   - `src\NebulaRAG.Cli\ragsettings.json`
   - `src\NebulaRAG.Cli\ragsettings.local.json`
2. If local password is a placeholder, ask user for it and update only that value.
3. Run:
   - `dotnet run --project src\NebulaRAG.Cli -- init`
4. Return success/failure with exact command output summary.
