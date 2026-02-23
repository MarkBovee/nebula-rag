# Nebula RAG

Nebula RAG is a lightweight PostgreSQL + `pgvector` retrieval system for Copilot and automation workflows.
It ships with a .NET CLI, an MCP server, and a Home Assistant add-on package.

## Highlights

- PostgreSQL-backed chunk + embedding storage.
- Fast vector retrieval using cosine distance and IVFFlat indexing.
- One command path for local development, container workflows, and Home Assistant hosting.
- RAG-first Copilot instruction assets included under `.github/`.

## Repository Layout

- `src/NebulaRAG.Core`: Chunking, embeddings, storage, and service logic.
- `src/NebulaRAG.Cli`: Local and automation command-line interface.
- `src/NebulaRAG.Mcp`: MCP server exposing `query_project_rag` and admin tools.
- `src/NebulaRAG.AddonHost`: Long-running web host for Home Assistant UI + MCP-over-HTTP.
- `nebula-rag/`: Home Assistant add-on package.
- `docs/`: Architecture and production-readiness planning.
- `AGENTS.md`: Repository-level agent behavior and coding quality guide.

## Quick Start (CLI)

1. Configure database settings in `src/NebulaRAG.Cli/ragsettings.local.json`.
2. Run schema initialization.
3. Index a source path.
4. Query indexed content.

```powershell
dotnet run --project src\NebulaRAG.Cli -- init
dotnet run --project src\NebulaRAG.Cli -- index --source .
dotnet run --project src\NebulaRAG.Cli -- query --text "How is NebulaRAG configured?"
```

Optional flags:

```powershell
dotnet run --project src\NebulaRAG.Cli -- query --text "your question" --limit 8
dotnet run --project src\NebulaRAG.Cli -- init --config C:\path\to\ragsettings.custom.json
```

## Quick Start (MCP)

Run the MCP server directly:

```powershell
dotnet run --project src\NebulaRAG.Mcp -- --config src\NebulaRAG.Cli\ragsettings.json
```

Primary MCP tools:

- `query_project_rag`
- `rag_init_schema`
- `rag_health_check`
- `rag_server_info`
- `rag_index_stats`
- `rag_recent_sources`
- `rag_list_sources`
- `rag_index_path`
- `rag_upsert_source`
- `rag_delete_source`
- `rag_purge_all`

## Quick Start (Home Assistant Add-on)

1. Add custom repository: `https://github.com/MarkBovee/NebulaRAG`
2. Install `Nebula RAG` add-on.
3. Configure `database.*` options.
4. Start add-on and open ingress panel.
5. Use built-in web UI to query, index, list/delete sources, and purge.

Add-on endpoints:

- Web UI: Home Assistant ingress or `http://homeassistant.local:8099`
- MCP JSON-RPC: `http://homeassistant.local:8099/mcp`

Add-on package files:

- `repository.json`
- `nebula-rag/config.json`
- `nebula-rag/Dockerfile`
- `nebula-rag/run.sh`
- `nebula-rag/DOCS.md`

## Security and Public Repo Hardening

This public repository includes baseline hardening assets:

- `SECURITY.md`: Vulnerability reporting policy.
- `.github/CODEOWNERS`: Required maintainer ownership.
- `.github/dependabot.yml`: Weekly NuGet and GitHub Actions dependency updates.
- `.github/workflows/security.yml`: CodeQL and dependency vulnerability audit.
- `.github/workflows/ha-addon-validate.yml`: Add-on validation/build checks.

Recommended GitHub settings:

1. Enable branch protection on `main`.
2. Require pull request review and passing status checks.
3. Enable Dependabot security updates and secret scanning.
4. Enable private vulnerability reporting and security advisories.

## Configuration and Secrets

Use `.nebula.env` for local/container credentials:

```powershell
Copy-Item .env.example .nebula.env
```

Typical env keys:

- `NEBULARAG_Database__Host`
- `NEBULARAG_Database__Port`
- `NEBULARAG_Database__Database`
- `NEBULARAG_Database__Username`
- `NEBULARAG_Database__Password`
- `NEBULARAG_Database__SslMode`

Never commit credential-bearing files.

## Setup Script

Configure user MCP for Home Assistant-hosted MCP endpoint:

```powershell
pwsh -File .\scripts\setup-nebula-rag.ps1 -Mode User -InstallTarget HomeAssistantAddon -HomeAssistantMcpUrl http://homeassistant.local:8099/mcp -Force
```

Configure user MCP for local container transport instead:

```powershell
pwsh -File .\scripts\setup-nebula-rag.ps1 -Mode User -InstallTarget LocalContainer -CreateEnvTemplate -Force
```

## VS Code Tasks

Use `Tasks: Run Task` and run:

- `Nebula RAG: Init DB`
- `Nebula RAG: Index Workspace`
- `Nebula RAG: Query`

## CI Workflows

- `.github/workflows/ha-addon-validate.yml`: Add-on manifest + builder test.
- `.github/workflows/rag-reindex.yml`: Scheduled/manual repository indexing.
- `.github/workflows/security.yml`: Security analysis and dependency audit.

## Documentation

- `docs/ARCHITECTURE.md`
- `docs/PRODUCTION_READINESS_PLAN.md`
- `nebula-rag/README.md`
- `nebula-rag/DOCS.md`

## Versioning

When add-on behavior changes, bump `nebula-rag/config.json` and update `nebula-rag/CHANGELOG.md`.

```powershell
pwsh -File .\scripts\bump-ha-addon-version.ps1 -Part Patch
```
