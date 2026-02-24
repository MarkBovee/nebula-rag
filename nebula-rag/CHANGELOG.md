# Changelog

All notable changes to the Nebula RAG Home Assistant add-on are documented in this file.

The format is inspired by Keep a Changelog and follows semantic versioning.

## [0.2.30] - 2026-02-24

- Improved MCP memory retrieval reliability: `memory_recall` now falls back to recent-memory listing when semantic recall returns zero matches, so stored entries remain discoverable.
- Added optional `sessionId` filtering to both `memory_recall` and `memory_list` flows, while preserving global read behavior when `sessionId` is not supplied.
- Updated MCP `tools/list` input schemas for memory tools to document `sessionId`, `type`, `tag`, and `limit` arguments.

## [0.2.29] - 2026-02-24

- Added optional `projectName` input support to MCP indexing tools (`rag_index_path`, `rag_index_text`, `rag_index_url`, and `rag_reindex_source`) so clients can provide explicit project grouping without relying only on path inference.
- Updated MCP `tools/list` input schemas to publish indexing argument contracts, including required fields and optional `projectName` metadata.
- Preserved backward compatibility by keeping existing source-path normalization and using it as fallback when `projectName` is not supplied.

## [0.2.28] - 2026-02-24

- Added dashboard Playwright iteration scripts: `npm run test:visual:ui` for interactive rerun workflows and `npm run test:visual:loop` for repeated headless dashboard data/mobile checks.
- Updated dashboard README visual-testing section with the new loop workflow commands for faster UI adjustment cycles.

## [0.2.27] - 2026-02-24

- Added a new Playwright functional dashboard suite that validates all major data surfaces (overview metrics, tab content, search results, source rows, activity items, performance panel, and memory metrics).
- Added explicit mobile responsiveness coverage in Playwright to verify stacked search controls and no horizontal overflow at narrow viewport widths.
- Improved dashboard responsiveness with targeted CSS for mobile tab navigation, search form stacking, and long source-path wrapping.
- Added stable `data-testid` hooks across dashboard sections and cards to support deterministic UI and data-element testing.

## [0.2.26] - 2026-02-24

- Added backend runtime telemetry plumbing so MCP tool calls are captured as dashboard activity events and surfaced in the Activity feed.
- Fixed dashboard semantic search wiring by aligning the UI with the API query response contract (`matches`, `chunkText`, `chunkIndex`), restoring visible search results.
- Improved performance timeline signal quality by recording query latency and indexing throughput samples from API and MCP execution paths and preserving decimal precision in chart rendering.
- Improved Memory Top Tags visualization readability with normalized tag labels and a vertical bar layout.

## [0.2.25] - 2026-02-24

- Removed Memory widgets from the `Overview` dashboard page so memory analytics are shown only in the dedicated `Memory` tab.
- Improved dashboard project grouping for multi-project workspaces by mapping paths like `workspace-notes/<project>/...` (and other generic root folders) to the nested project folder instead of collapsing them into one project bucket.

## [0.2.24] - 2026-02-24

- Added full memory analytics to backend storage/services with aggregated totals, 24h activity, distinct sessions, average tags, type distribution, top tags, daily growth series, and recent-session summaries.
- Added API endpoint `GET /api/memory/stats` and expanded dashboard snapshot payload to include `memoryStats`.
- Added a full `Memory Insights` dashboard experience (overview integration + dedicated tab) with KPI tiles, type distribution chart, top-tags chart, daily growth trend, and recent-session table.

## [0.2.23] - 2026-02-24

- Strengthened repository memory guidance across `AGENTS.md`, `.github/copilot-instructions.md`, `.github/instructions/rag.instructions.md`, and `.github/skills/nebularag/SKILL.md` with explicit Nebula memory recall/store cadence and secret-safe memory rules.
- Updated `scripts/setup-nebula-rag.ps1` project setup messaging so generated projects explicitly inherit the new memory cadence and Nebula skill workflow.
- Fixed PostgreSQL memory query/update parameter typing in `PostgresRagStore` to avoid `42P08` type-inference failures when nullable memory filters/fields are passed.

## [0.2.22] - 2026-02-24

- Updated repository coding instructions to explicitly require latest stable dependency versions (unless documented constraints apply) and to enforce switch/pattern-matching plus small-helper decomposition for large dispatch methods.
- Refactored MCP `ExecuteToolAsync` into a switch-based dispatcher with focused helper methods per tool, reducing method complexity and deep `if/else` nesting.

## [0.2.21] - 2026-02-24

- Fixed source-path normalization for containerized MCP text indexing so generic runtime roots (for example `/app`) are no longer used as project prefixes.
- Added MCP tool `rag_normalize_source_paths` to migrate existing stored source keys and remove duplicates after normalization changes.
- Replaced dashboard `Performance Timeline` mock values with sampled backend metrics (query latency rolling average, indexing docs/sec delta, and process CPU usage), and rounded chart values to integers for readability.
- Added OpenTelemetry tracing/metrics instrumentation to Add-on Host with optional OTLP export via add-on setting `telemetry.otlp_endpoint`.

## [0.2.20] - 2026-02-24

- Added backend `projectCount` to index stats, derived from distinct project keys in indexed source paths (file and URL sources).
- Dashboard now displays project count in both the top summary strip and `Index Health` panel.
- Completed source-ops cleanup flow:
	- `Source Breakdown` now groups by project using source counts.
	- `Sources` tab now includes a project filter for scoped management actions.

## [0.2.19] - 2026-02-24

- Fixed dashboard `Index Size` visibility by including index-size stats in aggregated snapshot responses with dedicated short-lived caching for expensive size queries.
- Reworked `Source Breakdown` to group by project and visualize source-count distribution per project (with project-level counts in legend/tooltip).
- Added a project filter to `Sources` management so operators can quickly scope source operations per project.

## [0.2.18] - 2026-02-24

- Refined the overview layout so `Source Breakdown` aligns with the top metrics row by spanning the two right-side columns on wide screens.
- Added responsive breakpoints so the overview gracefully collapses to full-width cards on medium and small screens.

## [0.2.17] - 2026-02-24

- Expanded the overview `Source Breakdown` panel width with a dedicated two-column dashboard grid so the chart uses available screen space instead of leaving an empty right-side column on wide layouts.
- Added responsive fallback so the overview stacks to a single column on smaller screens.

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
