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

For containerized MCP usage, prefer `.nebula.env` from `.env.example`:

```powershell
Copy-Item .env.example .nebula.env
```

## Documentation

Deep-dive documentation is in `docs/`:

- `docs/ARCHITECTURE.md`
- `docs/PRODUCTION_READINESS_PLAN.md`

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

- `rag_init_schema`
- `query_project_rag` (supports `text`, optional `limit`, optional `sourcePathContains`, optional `minScore`)
- `rag_health_check`
- `rag_server_info`
- `rag_index_stats`
- `rag_recent_sources`
- `rag_list_sources`
- `rag_index_path` (indexes a caller or server directory path; in container mode caller paths are mapped via `NEBULARAG_PathMappings`, e.g. `C:\project=/workspace`)
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

After MCP tool changes, rebuild the image and restart MCP clients so `tools/list` reflects the latest server surface.

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
npx -y @modelcontextprotocol/inspector --cli podman run --rm -i --pull=never --env-file .nebula.env localhost/nebula-rag-mcp:latest --skip-self-test --method tools/list
npx -y @modelcontextprotocol/inspector --cli podman run --rm -i --pull=never --env-file .nebula.env localhost/nebula-rag-mcp:latest --skip-self-test --method tools/call --tool-name rag_server_info
```

`.nebula.env` should contain runtime variables like:

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
- `NEBULARAG_PathMappings` (optional caller-to-runtime path mappings: `callerPrefix=runtimePrefix;caller2=runtime2`)

### VS Code Copilot MCP config

`.vscode\mcp.json` is included and points to the Nebula RAG MCP container launcher.

### Copilot CLI MCP config

Use `copilot.mcp.json` as your MCP server config when starting/configuring Copilot CLI.

## Use In Any Project

Single setup script (global + project):

```powershell
pwsh -File .\scripts\setup-nebula-rag.ps1 -Mode Both -TargetPath C:\path\to\your-project -Channel Auto -CreateEnvTemplate
```

`setup-nebula-rag.ps1` now asks which install target you want:

- `HomeAssistantAddon` (default/recommended)
- `LocalContainer` (legacy Podman MCP path)

You can also set it explicitly:

```powershell
pwsh -File .\scripts\setup-nebula-rag.ps1 -InstallTarget HomeAssistantAddon -Mode Project -TargetPath C:\path\to\your-project
pwsh -File .\scripts\setup-nebula-rag.ps1 -InstallTarget LocalContainer -Mode Both -CreateEnvTemplate
```

Common options:

```powershell
pwsh -File .\scripts\setup-nebula-rag.ps1 -Mode User -Channel Both -Force
pwsh -File .\scripts\setup-nebula-rag.ps1 -Mode Project -TargetPath C:\path\to\your-project
pwsh -File .\scripts\setup-nebula-rag.ps1 -Mode User -UserConfigPath C:\Users\you\AppData\Roaming\Code\User\mcp.json -EnvFilePath C:\Users\you\.nebula-rag\.nebula.env
```

What it does:

- Merges (or creates) user `mcp.json` server entries under both `mcpServers` and `servers`.
- Adds/updates a `nebula-rag` stdio server definition using Podman.
- Backs up existing config to `mcp.json.bak` before writing.
- Creates/updates project guidance files:

- `.github/copilot-instructions.md`
- `.github/instructions/rag.instructions.md`
- `.github/skills/nebularag/SKILL.md` (unless `-SkipSkill`)
- `.env.example` (if available)
- `.gitignore` entry for `.nebula.env`
- With `-CreateEnvTemplate`, writes `-EnvFilePath` by hard-copying repo root `.nebula.env` when present; otherwise falls back to `.env.example`.

Project MCP config files are maintained in this repository and should not be regenerated by the setup script.

1. Build the image once (or publish it to your own registry):

```powershell
podman build -t localhost/nebula-rag-mcp:latest -f Dockerfile .
```

2. In each target project, place a project-local `.nebula.env` (copy from `NebulaRAG/.env.example`).
3. Add an MCP entry that uses that project-local `.nebula.env`:

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
        "--env-file", ".nebula.env",
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

## Home Assistant add-on

This repository now includes a Home Assistant add-on package at:

- `repository.json`
- `nebula-rag/config.json`
- `nebula-rag/Dockerfile`
- `nebula-rag/run.sh`

`nebula-rag/Dockerfile` builds the add-on by cloning this repository at build time (defaults from `nebula-rag/build.yaml`).

The add-on is built as a one-shot job (`startup: once`) so you can run NebulaRAG operations from Home Assistant on demand.

### Add-on settings and old env mapping

The old local env settings are available in add-on options for the core runtime values:

- `database.host` -> `NEBULARAG_Database__Host`
- `database.port` -> `NEBULARAG_Database__Port`
- `database.name` -> `NEBULARAG_Database__Database`
- `database.username` -> `NEBULARAG_Database__Username`
- `database.password` -> `NEBULARAG_Database__Password`
- `database.ssl_mode` -> `NEBULARAG_Database__SslMode`

Related add-on options:

- `config_path` (passes `--config` to CLI)
- `source_path` (for `index` operation)

`NEBULARAG_PathMappings` is MCP-specific and not used by the Home Assistant add-on CLI workflow.

Supported `operation` values in add-on options:

- `init`
- `index`
- `query`
- `stats`
- `list-sources`
- `health-check`

Typical flow in Home Assistant:

1. Add your custom add-on repository: `https://github.com/MarkBovee/NebulaRAG`.
2. Install `Nebula RAG` add-on.
3. Configure PostgreSQL settings in add-on options.
4. Run with `operation=init` once.
5. Run with `operation=index` and `source_path=/share` (or another mounted path).
6. Use `query`/`stats`/`list-sources` as needed.

### Bumping add-on version

When you change add-on behavior, bump `nebula-rag/config.json` version before publishing updates.

Patch bump:

```powershell
pwsh -File .\scripts\bump-ha-addon-version.ps1 -Part Patch
```

Minor bump:

```powershell
pwsh -File .\scripts\bump-ha-addon-version.ps1 -Part Minor
```

Explicit version:

```powershell
pwsh -File .\scripts\bump-ha-addon-version.ps1 -Version 0.2.0
```

After bumping, commit and push. Home Assistant will then detect the updated add-on version.

Add-on release notes live in `nebula-rag/CHANGELOG.md`.

### Add-on CI validation

GitHub Actions workflow `.github/workflows/ha-addon-validate.yml` validates add-on manifests and runs the Home Assistant builder in `--test` mode for `amd64` when add-on files change.
