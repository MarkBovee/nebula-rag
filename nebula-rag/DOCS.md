# Nebula RAG Add-on

Run NebulaRAG CLI operations from Home Assistant against your PostgreSQL/pgvector database.

## Installation

1. In Home Assistant, go to Settings -> Add-ons -> Add-on Store.
2. Add this repository URL as a custom add-on repository:
   - `https://github.com/MarkBovee/NebulaRAG`
3. Open the `Nebula RAG` add-on and install it.

## Configuration

Set database settings in add-on configuration:

- `database.host`
- `database.port`
- `database.name`
- `database.username`
- `database.password`
- `database.ssl_mode`

## Operations

Use `operation` to control what runs:

- `init`
- `index`
- `query`
- `stats`
- `list-sources`
- `health-check`

### Common usage

- First run: `operation=init`
- Index files: `operation=index`, `source_path=/share`
- Query: `operation=query`, set `query_text`

## Notes

- This add-on is `startup: once` and intended for on-demand jobs.
- Add-on logs in Home Assistant show command output and errors.
- Change history is tracked in `CHANGELOG.md`.
