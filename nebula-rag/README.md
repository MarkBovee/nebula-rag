# Home Assistant Add-on: Nebula RAG

This folder contains a complete Home Assistant add-on package for running NebulaRAG CLI operations against PostgreSQL/pgvector.

## Folder layout

- `../repository.json`: Custom add-on repository metadata.
- `config.json`: Add-on manifest and option schema.
- `DOCS.md`: Home Assistant add-on documentation page.
- `Dockerfile`: Add-on image build.
- `run.sh`: Runtime entry script that maps add-on options to `NEBULARAG_*` environment variables.
- `build.yaml`: Optional build args for source repo URL/ref.

## Build source

The add-on Dockerfile clones NebulaRAG source during image build.

Default values:

- `NEBULARAG_REPO_URL=https://github.com/MarkBovee/NebulaRAG.git`
- `NEBULARAG_REPO_REF=main`

You can change these in `build.yaml` to pin a tag, branch, or fork.

## Supported operations

Set `operation` in add-on options to one of:

- `init`
- `index`
- `query`
- `stats`
- `list-sources`
- `health-check`

## Recommended option defaults

- `source_path`: `/share`
- `database.host`: your PostgreSQL host or IP
- `database.port`: `5432`
- `database.name`: your database name
- `database.username`: your database user
- `database.password`: your database password
- `database.ssl_mode`: `Prefer` or `Disable` (for trusted LAN)

## Versioning

When you change the add-on behavior, bump the version in `nebula-rag/config.json` and add notes to `CHANGELOG.md`.

Use the helper script from repo root:

```powershell
pwsh -File .\scripts\bump-ha-addon-version.ps1 -Part Patch
```

Or set an explicit version:

```powershell
pwsh -File .\scripts\bump-ha-addon-version.ps1 -Version 0.2.0
```

After bumping, commit and push. In Home Assistant, update the add-on from your repository.

## Validation

Official Home Assistant testing/validation docs:

- https://developers.home-assistant.io/docs/apps/testing/
- https://developers.home-assistant.io/docs/apps/configuration/

Recommended local add-on build validation uses the Home Assistant builder image (`--test`) from the add-on docs.
