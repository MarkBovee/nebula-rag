# Nebula RAG Add-on

Run NebulaRAG as a long-running Home Assistant service with:

- Nebula-themed web UI for query/index/management
- MCP-over-HTTP endpoint for remote MCP clients

## Installation

1. Open Home Assistant: `Settings -> Add-ons -> Add-on Store`.
2. Add custom add-on repository URL:
   - `https://github.com/MarkBovee/NebulaRAG`
3. Open `Nebula RAG` and install.

## Configuration

Configure database options in add-on settings:

- `database.host`
- `database.port`
- `database.name`
- `database.username`
- `database.password`
- `database.ssl_mode`

Optional:

- `default_index_path` (default path used in web UI index form)

## Web Interface

Open the add-on from Home Assistant sidebar (ingress) to access:

- Health check and index stats
- Semantic query panel
- Index path operations
- Source listing and delete
- Full purge with explicit confirmation phrase

## MCP Endpoint

The add-on exposes MCP JSON-RPC at:

- `http://homeassistant.local:8099/mcp`

Supported methods:

- `initialize`
- `ping`
- `tools/list`
- `tools/call`

Key tools include:

- `query_project_rag`
- `rag_init_schema`
- `rag_health_check`
- `rag_server_info`
- `rag_index_stats`
- `rag_list_sources`
- `rag_index_path`
- `rag_delete_source`
- `rag_purge_all`

## Troubleshooting

`Cannot load library libgssapi_krb5.so.2`:

- Fixed in add-on version `0.1.2` by installing `libgssapi-krb5-2` in runtime image.
- Update to latest add-on version and restart the job.

Database connection issues:

- Verify `database.host`, `database.port`, credentials, and SSL mode.
- Confirm PostgreSQL accepts network connections from Home Assistant host.

MCP client cannot connect:

- Ensure add-on is running in Home Assistant.
- Test MCP URL: `http://homeassistant.local:8099/mcp`.
- If using VS Code MCP HTTP config, confirm server entry uses `type: "http"` and the correct URL.

## Notes

- Add-on is `startup: services` and runs continuously.
- Logs include API/MCP operation details and errors.
- Release history: `CHANGELOG.md`.
