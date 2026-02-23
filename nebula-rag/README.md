# Home Assistant Add-on: Nebula RAG

This directory contains the full Home Assistant add-on package for a long-running NebulaRAG host that serves:

- a Nebula-themed browser UI
- an MCP-over-HTTP endpoint (`/mcp`)

## Package Contents

- `../repository.json`: Add-on repository metadata.
- `config.json`: Add-on manifest, options, and schema.
- `DOCS.md`: Home Assistant-facing usage guide.
- `Dockerfile`: Multi-stage add-on image build.
- `run.sh`: Runtime option-to-env mapping and host startup.
- `build.yaml`: Build arguments for repository URL and ref pinning.
- `icon.png` / `logo.png`: Add-on branding assets.

## Build Source and Pinning

The image build clones this repository by default:

- `NEBULARAG_REPO_URL=https://github.com/MarkBovee/NebulaRAG.git`
- `NEBULARAG_REPO_REF=main`

Change `build.yaml` to pin a release tag, branch, or fork.

## Runtime Surface

- Web UI (Home Assistant ingress and optional exposed port)
- MCP endpoint: `http://homeassistant.local:8099/mcp`

## Required Configuration

- `database.host`
- `database.port`
- `database.name`
- `database.username`
- `database.password`
- `database.ssl_mode`

Recommended defaults:

- `default_index_path=/share`
- `database.port=5432`
- `database.ssl_mode=Prefer` (or `Disable` on trusted LAN setups)

## Runtime Notes

- Runtime image installs `libgssapi-krb5-2` to satisfy .NET native dependency loading.
- Service listens on port `8099`.
- Home Assistant ingress is enabled in `config.json`.

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
