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

For containerized MCP usage, prefer `.env` from `.env.example`:

```powershell
Copy-Item .env.example .env
```

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
- `rag_list_sources`
- `rag_index_path` (indexes a server-visible directory path)
- `rag_upsert_source` (indexes provided `sourcePath` + `content` text directly)
- `rag_delete_source` (requires `sourcePath` + `confirm=true`)
- `rag_purge_all` (requires `confirmPhrase="PURGE ALL"`)

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

Build the MCP image directly with the Dockerfile:

```powershell
podman build -t localhost/nebula-rag-mcp:latest -f Dockerfile .
```

Pass through MCP arguments to the containerized server:

```powershell
pwsh -File .\scripts\start-nebula-rag-mcp-container.ps1 --self-test-only
```

### MCP testing tools (recommended)

Use the official MCP Inspector to test capabilities, `tools/list`, and `tools/call`:

```powershell
npx @modelcontextprotocol/inspector --config .vscode/mcp.json --server "nebula-rag"
```

CLI-only smoke tests with Inspector:

```powershell
npx -y @modelcontextprotocol/inspector --cli podman run --rm -i --pull=never --env-file .env localhost/nebula-rag-mcp:latest --skip-self-test --method tools/list
npx -y @modelcontextprotocol/inspector --cli podman run --rm -i --pull=never --env-file .env localhost/nebula-rag-mcp:latest --skip-self-test --method tools/call --tool-name rag_server_info
```

`.env` should contain runtime variables like:

```dotenv
NEBULARAG_Database__Host=192.168.1.135
NEBULARAG_Database__Database=brewmind
NEBULARAG_Database__Username=postgres
NEBULARAG_Database__Password=<password>
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

## Use In Any Project

Single setup script (global + project):

```powershell
pwsh -File .\scripts\setup-nebula-rag.ps1 -Mode Both -TargetPath C:\path\to\your-project -Channel Auto -CreateEnvTemplate
```

Common options:

```powershell
pwsh -File .\scripts\setup-nebula-rag.ps1 -Mode User -Channel Both -Force
pwsh -File .\scripts\setup-nebula-rag.ps1 -Mode Project -TargetPath C:\path\to\your-project
pwsh -File .\scripts\setup-nebula-rag.ps1 -Mode User -UserConfigPath C:\Users\you\AppData\Roaming\Code\User\mcp.json -EnvFilePath C:\Users\you\.nebula-rag\.env
```

What it does:

- Merges (or creates) user `mcp.json` server entries under both `mcpServers` and `servers`.
- Adds/updates a `nebula-rag` stdio server definition using Podman.
- Backs up existing config to `mcp.json.bak` before writing.
- Creates/updates project files:

- `.vscode/mcp.json`
- `copilot.mcp.json`
- `.github/copilot-instructions.md`
- `.github/instructions/rag.instructions.md`
- `.github/skills/nebularag/SKILL.md` (unless `-SkipSkill`)
- `.env.example` (if available)
- `.gitignore` entry for `.env`
- Optionally writes an env template at `-EnvFilePath` when `-CreateEnvTemplate` is used.

1. Build the image once (or publish it to your own registry):

```powershell
podman build -t localhost/nebula-rag-mcp:latest -f Dockerfile .
```

2. In each target project, place a project-local `.env` (copy from `NebulaRAG/.env.example`).
3. Add an MCP entry that uses that project-local `.env`:

```json
{
  "mcpServers": {
    "nebula-rag": {
      "type": "stdio",
      "command": "podman",
      "args": [
        "run",
        "--rm",
        "-i",
        "--pull=never",
        "--memory=2g",
        "--cpus=1.0",
        "--env-file", ".env",
        "localhost/nebula-rag-mcp:latest",
        "--skip-self-test"
      ]
    }
  }
}
```

This keeps credentials out of config files and lets every project control its own DB target.

## .github Copilot automation assets

This repository now includes:

- `.github\copilot-instructions.md`
- `.github\instructions\rag.instructions.md`
- `.github\skills\nebularag\SKILL.md`

These files instruct Copilot agents to call `query_project_rag` first for project-context tasks.

## Workflow for continuous indexing

`.github\workflows\rag-reindex.yml` can reindex on schedule or manually.

Required GitHub secrets:

- `RAG_DB_HOST`
- `RAG_DB_NAME`
- `RAG_DB_USER`
- `RAG_DB_PASSWORD`
