# Nebula RAG

Nebula RAG is a lightweight PostgreSQL-backed RAG system for GitHub Copilot workflows.
It works from the command line and from VS Code tasks.

## What it does

- Stores chunked documents and vector embeddings in PostgreSQL (`pgvector`).
- Uses `ivfflat` cosine index for fast retrieval.
- Provides three CLI commands:
  - `init` to create schema/indexes
  - `index` to ingest files from a directory
  - `query` to retrieve the most relevant chunks

## Configuration

Settings are in:

- `src\NebulaRAG.Cli\ragsettings.json` (base settings)
- `src\NebulaRAG.Cli\ragsettings.local.json` (local secret overrides)

Set your database password in `ragsettings.local.json`.

## CLI usage

From project root:

```powershell
dotnet run --project src\NebulaRAG.Cli -- init
dotnet run --project src\NebulaRAG.Cli -- index --source .
dotnet run --project src\NebulaRAG.Cli -- query --text "How do we configure postgres?"
```

Optional:

```powershell
dotnet run --project src\NebulaRAG.Cli -- query --text "your question" --limit 8
dotnet run --project src\NebulaRAG.Cli -- index --source C:\path\to\code
dotnet run --project src\NebulaRAG.Cli -- init --config C:\path\to\custom-ragsettings.json
```

## VS Code usage

Open the command palette and run **Tasks: Run Task**, then choose:

- `Nebula RAG: Init DB`
- `Nebula RAG: Index Workspace`
- `Nebula RAG: Query`

These tasks are defined in `.vscode\tasks.json`.

## MCP wrapper (automatic Copilot RAG access)

Nebula RAG includes an MCP server in `src\NebulaRAG.Mcp` that exposes tool:

- `query_project_rag` (supports `text`, optional `limit`, optional `sourcePathContains`, optional `minScore`)
- `rag_health_check`
- `rag_server_info`
- `rag_index_stats`
- `rag_recent_sources`

Run it directly:

```powershell
dotnet run --project src\NebulaRAG.Mcp -- --config src\NebulaRAG.Cli\ragsettings.json
```

### Containerized MCP server (Docker/Podman)

Build and run one-off self-tests in containers:

```powershell
docker compose up --build mcp-self-test
podman compose up --build mcp-self-test
```

The MCP server now runs startup self-tests by default:

- Initializes schema if needed.
- Executes a smoke query against PostgreSQL.

To run MCP via container stdio from this repo:

```powershell
pwsh -File .\scripts\start-nebula-rag-mcp-container.ps1
```

Pass through MCP arguments to the containerized server:

```powershell
pwsh -File .\scripts\start-nebula-rag-mcp-container.ps1 --self-test-only
```

### MCP testing tools (recommended)

Use the official MCP Inspector to test capabilities, `tools/list`, and `tools/call`:

```powershell
npx @modelcontextprotocol/inspector --config .vscode/mcp.json --server "Nebula RAG"
```

CLI-only smoke tests with Inspector:

```powershell
npx @modelcontextprotocol/inspector --cli --config .vscode/mcp.json --server "Nebula RAG" --method tools/list
npx @modelcontextprotocol/inspector --cli --config .vscode/mcp.json --server "Nebula RAG" --method tools/call --tool-name rag_health_check
npx @modelcontextprotocol/inspector --cli --config .vscode/mcp.json --server "Nebula RAG" --method tools/call --tool-name query_project_rag --tool-arg text="Where is RagQueryService used?"
```

Optional environment overrides for container/runtime config:

- `NEBULARAG_Database__Host`
- `NEBULARAG_Database__Port`
- `NEBULARAG_Database__Database`
- `NEBULARAG_Database__Username`
- `NEBULARAG_Database__Password`
- `NEBULARAG_Database__SslMode`
- `NEBULARAG_CONFIG`

### VS Code Copilot MCP config

`.vscode\mcp.json` is included and points to the Nebula RAG MCP container launcher.

### Copilot CLI MCP config

Use `copilot.mcp.json` as your MCP server config when starting/configuring Copilot CLI.

## .github Copilot automation assets

This repository now includes:

- `.github\copilot-instructions.md`
- `.github\instructions\rag.instructions.md`
- `.github\prompts\rag-first.prompt.md`
- `.github\prompts\rag-setup.prompt.md`
- `.github\prompts\rag-init.prompt.md`
- `.github\prompts\rag-index.prompt.md`
- `.github\skills\nebularag\SKILL.md`

These files instruct Copilot agents to call `query_project_rag` first for project-context tasks.

### Slash prompts you can run

- `/rag-setup` -> validates config, runs init, runs index, runs smoke query
- `/rag-init` -> runs DB schema init
- `/rag-index` -> runs indexing and query verification

## Workflow for continuous indexing

`.github\workflows\rag-reindex.yml` can reindex on schedule or manually.

Required GitHub secrets:

- `RAG_DB_HOST`
- `RAG_DB_NAME`
- `RAG_DB_USER`
- `RAG_DB_PASSWORD`
