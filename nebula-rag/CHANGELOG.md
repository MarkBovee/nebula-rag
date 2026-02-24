# Changelog

All notable changes to the Nebula RAG Home Assistant add-on are documented in this file.

The format is inspired by Keep a Changelog and follows semantic versioning.

## [0.2.16] - 2026-02-24

- Fixed a persistent stale-image risk in add-on builds: the repository clone layer now incorporates Home Assistant `BUILD_VERSION`, so version bumps invalidate Docker cache and refresh `main` instead of reusing an older clone layer.
- Added `/app/nebularag_commit.txt` to runtime images so deployed containers can be traced to the exact cloned commit during diagnostics.
- Startup logs now print the baked source commit hash, making stale-deployment detection visible directly in Home Assistant add-on logs.

## [0.2.15] - 2026-02-24

- Add-on host now sets `no-store/no-cache` headers for dashboard `index.html` responses so updated deployments always load the latest hashed JS/CSS bundles instead of stale cached shells.
- Clarified deployment troubleshooting for stale dashboards: add-on image contents are built from `NEBULARAG_REPO_REF` (`main` by default), so local unpushed changes are not included until pushed or ref-pinned.

## [0.2.14] - 2026-02-24

- Replaced the root `README.md` with a GitHub-focused system overview that clearly explains architecture, runtime modes, and usage paths.
- Added a single one-line remote installer command that downloads and runs `scripts/setup-nebula-rag.ps1` for user-level MCP setup.

## [0.2.13] - 2026-02-24

- Added explicit memory routing policy in `AGENTS.md` to separate VS Code user memory from Nebula project memory.
- Updated `rag.instructions.md` and `copilot-instructions.md` to require memory-assisted retrieval and clear fallback/precedence behavior.
- Updated `.github/skills/nebularag/SKILL.md` to include `memory_recall` and `memory_store` in the standard workflow.
- Updated `scripts/setup-nebula-rag.ps1` messaging so global/project setup explicitly indicates memory routing templates are applied.

## [0.2.12] - 2026-02-24

- Consolidated MCP transport and tool execution into a single shared implementation in `NebulaRAG.Core` to eliminate drift between `AddonHost` and `NebulaRAG.Mcp`.
- Refactored `McpTransportHandler` into focused partial files (routing, tool execution, and JSON-RPC envelope helpers) to improve maintainability.
- Reworked `NebulaRAG.Mcp` into a thin stdio wrapper that delegates JSON-RPC handling to the shared Core handler.
- Improved `query_project_rag` UX: empty `arguments.text` now returns a usage hint instead of an error response to reduce noisy repeated failure messages.

## [0.2.11] - 2026-02-24

- Updated dashboard overview layout so `Source Breakdown` sits next to `Index Health` instead of spanning a full-width row.
- Rebuilt add-on dashboard static assets for deployment parity between local and Home Assistant-hosted UI.
- Updated repository instruction files to require a Home Assistant add-on version bump and changelog entry after each implemented change.

## [0.2.10] - 2026-02-24

- Updated source-path normalization to prefix indexed keys with the project folder name (for example `NebulaRAG/src/...`).
- Preserved URL-based source keys while normalizing file-system paths.
- Re-synchronized `rag-sources.md` after source key migration.

## [0.2.9] - 2026-02-24

- Normalized indexed source keys to project-relative paths for cleaner MCP source output.
- Improved ingestion defaults to skip generated/compiled artifacts during indexing.
- Added source normalization tooling to migrate existing absolute-path source records.

## [0.2.8] - 2026-02-24

- Refactored add-on REST API endpoints into a dedicated controller (`RagApiController`) to improve maintainability as the API surface grows.
- Added a dedicated dashboard snapshot service with short-lived caching for health/stats to reduce repeated polling load.
- Optimized source listing and stats paths:
	- source limit is now applied in SQL instead of in-memory trimming
	- expensive index-size calculation is now optional (`/api/stats?includeSize=true`)
- Setup script efficiency improvements:
	- skip copying instruction/baseline files when destination content is unchanged
	- removed unused setup helper code.

## [0.2.7] - 2026-02-24

- Updated setup script defaults for privacy-first usage: Home Assistant MCP URL now defaults to local network path-base endpoint (`http://homeassistant.local:8099/nebula/mcp`).
- Added explicit external MCP URL option in setup script (`-UseExternalHomeAssistantUrl` with `-ExternalHomeAssistantMcpUrl`) so public ingress usage is opt-in.
- Added a hard safety guard for external mode: `-UseExternalHomeAssistantUrl` now also requires `-ForceExternal` to prevent accidental public exposure.

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
