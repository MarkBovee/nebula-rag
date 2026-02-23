# Home Assistant Add-on: Nebula RAG

This directory contains the full Home Assistant add-on package used to run NebulaRAG CLI jobs against PostgreSQL/pgvector.

## Package Contents

- `../repository.json`: Add-on repository metadata.
- `config.json`: Add-on manifest, options, and schema.
- `DOCS.md`: Home Assistant-facing usage guide.
- `Dockerfile`: Multi-stage add-on image build.
- `run.sh`: Runtime option-to-env mapping and command dispatcher.
- `build.yaml`: Build arguments for repository URL and ref pinning.

## Build Source and Pinning

The image build clones this repository by default:

- `NEBULARAG_REPO_URL=https://github.com/MarkBovee/NebulaRAG.git`
- `NEBULARAG_REPO_REF=main`

Change `build.yaml` to pin a release tag, branch, or fork.

## Supported Operations

Set `operation` in add-on options to one of:

- `init`
- `index`
- `query`
- `stats`
- `list-sources`
- `health-check`

## Required Configuration

- `database.host`
- `database.port`
- `database.name`
- `database.username`
- `database.password`
- `database.ssl_mode`

Recommended defaults:

- `source_path=/share`
- `database.port=5432`
- `database.ssl_mode=Prefer` (or `Disable` on trusted LAN setups)

## Runtime Dependency Note

The runtime image installs `libgssapi-krb5-2` to satisfy .NET database client native dependency loading and avoid `libgssapi_krb5.so.2` warnings in logs.

## Release Process

When add-on behavior changes:

1. Bump `nebula-rag/config.json` version.
2. Update `nebula-rag/CHANGELOG.md`.
3. Commit and push.

Patch bump:

```powershell
pwsh -File .\scripts\bump-ha-addon-version.ps1 -Part Patch
```

Explicit version:

```powershell
pwsh -File .\scripts\bump-ha-addon-version.ps1 -Version 0.2.0
```

## Validation

Validation is automated in `.github/workflows/ha-addon-validate.yml` and includes:

- JSON manifest validation.
- Required file checks.
- Home Assistant builder `--test` image build for `amd64`.
