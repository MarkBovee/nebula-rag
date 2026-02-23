# Nebula RAG Add-on

Run NebulaRAG CLI operations from Home Assistant against your PostgreSQL/pgvector database.

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

Optional operation inputs:

- `source_path` (used by `index`)
- `query_text` and `query_limit` (used by `query`)
- `config_path` (optional CLI config override)

## Operations

Use `operation` to run one job per start:

- `init`: Initialize schema and indexes.
- `index`: Index files from `source_path`.
- `query`: Query indexed content using `query_text`.
- `stats`: Show index statistics.
- `list-sources`: List indexed source paths.
- `health-check`: Validate DB connectivity.

## Typical Flow

1. Set database credentials.
2. Run `operation=init` once.
3. Run `operation=index` with `source_path=/share`.
4. Run `operation=query` with `query_text`.

## Troubleshooting

`Cannot load library libgssapi_krb5.so.2`:

- Fixed in add-on version `0.1.2` by installing `libgssapi-krb5-2` in runtime image.
- Update to latest add-on version and restart the job.

Database connection issues:

- Verify `database.host`, `database.port`, credentials, and SSL mode.
- Confirm PostgreSQL accepts network connections from Home Assistant host.

## Notes

- Add-on is `startup: once` for on-demand operations.
- Logs show executed command output and errors.
- Release history: `CHANGELOG.md`.
