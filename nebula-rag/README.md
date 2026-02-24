# Home Assistant Add-on: Nebula RAG

Nebula RAG is a Home Assistant add-on that runs a long-lived NebulaRAG host inside a container. The add-on provides:

- A browser-first dashboard for inspecting indexed sources, index health, and retrieval results.
- An MCP-over-HTTP endpoint for agent integrations and editor tooling.

**Key features**

- Persistent RAG service backed by PostgreSQL + `pgvector` for vector similarity queries.
- One-click Home Assistant installation via this repository package.
- Built-in dashboard to view sources, perform queries, and inspect chunk-level results.
- MCP JSON-RPC endpoint for integrating external agents and IDE extensions.

**Who should use this add-on?**

- Developers who need fast, project-aware context lookups inside Home Assistant.
- Teams who want a local-first RAG endpoint for automating changelogs, PR drafting, or code-aware Q&A.

## Quick Start

1. Add this repository to Home Assistant add-on repositories and install the `Nebula RAG` add-on.
2. Configure the add-on's `database.*` settings to point to a PostgreSQL instance with `pgvector` installed.
3. Start the add-on and open the add-on ingress page to access the dashboard.

Common quick commands (from the repository root):

```powershell
dotnet run --project src\NebulaRAG.Cli -- init
dotnet run --project src\NebulaRAG.Cli -- index --source .
dotnet run --project src\NebulaRAG.Cli -- query --text "How is MCP transport handled?"
```

## Configuration

Required fields (in `config.json` add-on options):

- `database.host` — PostgreSQL host
- `database.port` — PostgreSQL port (default: `5432`)
- `database.name` — database name
- `database.username` — DB user
- `database.password` — DB password
- `database.ssl_mode` — `Disable`/`Prefer`/`Require` (recommended: `Prefer`)

Recommended options:

- `default_index_path` — path to index inside the container (default: `/share`)
- `path_base` — optional route prefix (e.g., `/nebula`) to expose UI and MCP under a subpath
- `telemetry.otlp_endpoint` — optional OTLP collector URL for traces/metrics

Security notes:

- Do not expose the PostgreSQL instance directly to the public internet.
- Use Home Assistant ingress for UI access rather than exposing the service port when possible.

## Endpoints

- UI root: `http://<host>:8099/` (ingress will provide a friendly path)
- Dashboard: `http://<host>:8099/dashboard/`
- MCP JSON-RPC: `http://<host>:8099/mcp`

If `path_base` is set to `/nebula`, paths become `/nebula/`, `/nebula/dashboard/`, and `/nebula/mcp`.

## Packaging & Build

By default the build clones this repository:

- `NEBULARAG_REPO_URL=https://github.com/MarkBovee/NebulaRAG.git`
- `NEBULARAG_REPO_REF=main`

Override `build.yaml` to pin a tag, branch, or fork for deterministic builds.

## Release & Validation

When changing add-on behavior:

1. Bump the add-on version in `nebula-rag/config.json`.
2. Add a matching entry to `nebula-rag/CHANGELOG.md`.
3. Commit, push, and create a release if applicable.

Use the helper script to bump versions:

```powershell
pwsh -File .\scripts\bump-ha-addon-version.ps1 -Part Patch
```

Validation is performed by `.github/workflows/ha-addon-validate.yml` and includes manifest checks and test builds.

## Troubleshooting

- Database migration failures: verify connectivity and that `pgvector` is installed in the target Postgres instance.
- No results from queries: confirm that sources were indexed (`index` CLI) and that `default_index_path` points to readable files.
- Ingress or route issues: confirm `path_base` is configured consistently and Home Assistant ingress is enabled.

## Further reading

- See `DOCS.md` for Home Assistant-specific run and configuration notes.
- See the top-level `README.md` for developer CLI usage, MCP tooling, and architecture details.

---
Updated to provide clearer guidance for installing and operating the Nebula RAG Home Assistant add-on.
