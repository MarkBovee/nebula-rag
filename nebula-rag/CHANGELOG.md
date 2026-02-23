# Changelog

All notable changes to the Nebula RAG Home Assistant add-on are documented in this file.

The format is inspired by Keep a Changelog and follows semantic versioning.

## [0.2.6] - 2026-02-23

- Added defensive numeric normalization in dashboard metrics so missing fields cannot trigger `toLocaleString` runtime crashes during mixed-version rollouts.
- Rebuilt dashboard assets (`index.CLholFkU.js`) for add-on deployment.

## [0.2.5] - 2026-02-23

- Fixed dashboard API routing under ingress/path-base hosting by switching frontend API calls to relative routes (`api/...`) so computed base prefixes are preserved.
- Prevents calls from bypassing ingress/path-base when hosted under routes like `/api/hassio_ingress/<token>/...` or `/<path_base>/...`.

## [0.2.4] - 2026-02-23

- Bumped add-on manifest version to `0.2.4`.
- Exposed host port mapping for `8099/tcp` by default in the add-on manifest so the web UI and MCP endpoint can be reached without ingress when desired.
- Backend: add-on now surfaces index storage size via API (`indexSizeBytes`) for dashboard health views.

## [0.2.3] - 2026-02-23

- Added optional `path_base` add-on setting to host Nebula RAG behind a route prefix (for example `/nebula`).
- Added AddonHost path-base support so endpoints can be exposed as `/nebula/dashboard/`, `/nebula/api/...`, and `/nebula/mcp`.
- Made dashboard the default web UI entry by redirecting root (`/`) to `dashboard/`.
- Updated dashboard build/runtime behavior for prefixed hosting:
	- API client now derives route prefix from current dashboard path.
	- Built dashboard assets now use relative paths for compatibility under prefixed routes.

## [0.2.2] - 2026-02-23

- Fixed add-on host startup failure by switching runtime image to `mcr.microsoft.com/dotnet/aspnet:10.0`.
- Resolved missing `Microsoft.AspNetCore.App` framework error when launching `NebulaRAG.AddonHost.dll`.

## [0.2.1] - 2026-02-23

- Hardened add-on Docker build restore flow for Home Assistant builders:
	- added low-memory restore flags (`--disable-parallel`)
	- added retry loop for transient restore failures
	- publish now uses `--no-restore` after successful restore
- Added explicit project file existence check before restore to fail fast with clearer diagnostics.

## [0.2.0] - 2026-02-23

- Switched add-on runtime from one-shot CLI jobs to a long-running service host.
- Added Nebula-themed web interface for query, indexing, and source management.
- Added MCP-over-HTTP endpoint at `/mcp` so MCP can run from Home Assistant.
- Added Home Assistant ingress/web UI configuration and exposed TCP port `8099`.
- Added add-on branding assets (`icon.png`, `logo.png`).

## [0.1.2] - 2026-02-23

- Added add-on runtime dependency `libgssapi-krb5-2` to avoid `libgssapi_krb5.so.2` load warnings.
- Public repository hardening update: added security policy, dependency automation, and security CI workflow.
- Documentation refresh for clearer setup, operations, troubleshooting, and release process.

## [0.1.1] - 2026-02-23

- Add-on install readiness update.
- Added `DOCS.md` for Home Assistant add-on documentation.
- Added add-on metadata (`url`, `stage`) in `config.json`.
- Restricted supported architectures to `amd64` and `aarch64`.

## [0.1.0] - 2026-02-23

- Initial Home Assistant add-on release.
- Added add-on manifest (`config.json`) with configurable PostgreSQL connection settings.
- Added one-shot operations: `init`, `index`, `query`, `stats`, `list-sources`, and `health-check`.
- Added add-on runtime script (`run.sh`) that maps Home Assistant options to `NEBULARAG_*` runtime settings.
- Added add-on build configuration (`build.yaml`) and container build (`Dockerfile`).
- Added version bump helper script (`scripts/bump-ha-addon-version.ps1`).
