# NebulaRAG

NebulaRAG is a production-oriented Retrieval Augmented Generation platform for code and project knowledge.

It combines:

- A .NET core retrieval engine.
- A local CLI for indexing and querying.
- MCP endpoints for agents and editor tooling.
- A Home Assistant add-on host with a built-in web experience.

## One-Line Remote Install (Windows + PowerShell)

```powershell
$scriptUrl='https://raw.githubusercontent.com/MarkBovee/NebulaRAG/main/scripts/setup-nebula-rag.ps1'; $scriptPath=Join-Path ([System.IO.Path]::GetTempPath()) 'setup-nebula-rag.ps1'; Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue; Invoke-WebRequest -Uri $scriptUrl -OutFile $scriptPath -ErrorAction Stop; & $scriptPath
```

Why this format:

- Works directly from an existing PowerShell session without nested command-quoting issues.
- Removes stale temp copies before download, then downloads and executes the installer script.

What it does:

- Downloads the official setup script from this repository.
- Configures user-level MCP registration for VS Code and Claude Code.
- Targets the Home Assistant add-on MCP endpoint by default.

## Why NebulaRAG

- Fast local-first retrieval over PostgreSQL + `pgvector`.
- Clean architecture split into core engine, transport adapters, and host runtime.
- Built-in operational workflow for RAG indexing, querying, and source management.
- Agent-ready conventions and instruction templates for RAG-first coding workflows.

## System Overview

1. Content is indexed into chunks with embeddings.
2. Chunks are stored in PostgreSQL and queried by vector similarity.
3. Results are exposed through CLI, MCP, and the add-on host UI.

Core projects:

- `src/NebulaRAG.Core`: chunking, embeddings, storage, query and management services.
- `src/NebulaRAG.Cli`: CLI entry points (`init`, `index`, `query`).
- `src/NebulaRAG.Mcp`: stdio MCP adapter for local tooling.
- `src/NebulaRAG.AddonHost`: HTTP host for Home Assistant ingress UI and MCP endpoint.
- `nebula-rag/`: Home Assistant add-on package and release metadata.

## Quick Start

### Local CLI

```powershell
dotnet run --project src\NebulaRAG.Cli -- init
dotnet run --project src\NebulaRAG.Cli -- index --source .
dotnet run --project src\NebulaRAG.Cli -- query --text "How is MCP transport handled?"
```

### MCP (stdio)

```powershell
dotnet run --project src\NebulaRAG.Mcp -- --config src\NebulaRAG.Cli\ragsettings.json
```

### Home Assistant Add-on

1. Add repository: `https://github.com/MarkBovee/NebulaRAG`
2. Install `Nebula RAG` add-on.
3. Set `database.*` options.
4. Start add-on and open ingress.

Typical endpoints:

- UI: `http://homeassistant.local:8099/nebula/`
- Dashboard: `http://homeassistant.local:8099/nebula/dashboard/`
- MCP JSON-RPC: `http://homeassistant.local:8099/nebula/mcp`

## MCP Tooling Surface

Key tools include:

- `query_project_rag`
- `rag_init_schema`
- `rag_health_check`
- `rag_index_stats`
- `rag_list_sources`
- `rag_index_path`
- `rag_index_text`
- `rag_index_url`
- `rag_reindex_source`
- `rag_get_chunk`
- `rag_search_similar`
- `rag_normalize_source_paths`
- `rag_delete_source`
- `rag_purge_all`
- `memory_store`
- `memory_recall`
- `memory_list`
- `memory_update`
- `memory_delete`

## Setup Script Examples

User-level setup from local clone:

```powershell
pwsh -File .\scripts\setup-nebula-rag.ps1 -Mode User -ClientTargets Both -InstallTarget HomeAssistantAddon -HomeAssistantMcpUrl http://homeassistant.local:8099/nebula/mcp -Force
```

Local container transport mode:

```powershell
pwsh -File .\scripts\setup-nebula-rag.ps1 -Mode User -InstallTarget LocalContainer -CreateEnvTemplate -Force
```

## Security

- Vulnerability policy: `SECURITY.md`
- Code ownership: `.github/CODEOWNERS`
- Dependency maintenance: `.github/dependabot.yml`
- Security workflow: `.github/workflows/security.yml`

Never commit secrets or credential-bearing env files.

## Contributing

1. Create a branch from `main`.
2. Keep changes scoped and follow repository instructions.
3. Run tests and checks.
4. Open a PR with a clear change summary.
