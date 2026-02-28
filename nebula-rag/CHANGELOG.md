# Changelog

All notable changes to the Nebula RAG Home Assistant add-on are documented in this file.

The format is inspired by Keep a Changelog and follows semantic versioning.

## [0.3.13] - 2026-02-28

- Fixed Home Assistant add-on root route ambiguity by removing explicit `GET /` mapping in AddonHost, so the catch-all dashboard route is the single resolver for `/` and ingress slug paths.
- Preserved `UseAntiforgery()` middleware ordering with interactive Razor endpoints to keep `/mcp` and dashboard routes stable under ingress path-base execution.

## [0.3.12] - 2026-02-28

- Refactored dashboard page orchestration by splitting tab UIs into dedicated Blazor components (`OverviewManagementTab`, `RagManagementTab`, `MemoryManagementTab`, `PlansManagementTab`) and reducing `Dashboard.razor` to a tab host.
- Added reusable confirmation modal component for destructive operations and wired confirmation flows for RAG purge/source-delete and memory delete actions.
- Kept live operational behavior intact while improving dashboard maintainability through component boundaries and per-tab state management.

## [0.3.11] - 2026-02-28

- Rebuilt the Blazor dashboard into a tabbed management console with category tabs: `Overview`, `RAG`, `Memory`, and `Plans`.
- Added RAG operations to the dashboard: semantic query execution, source indexing, source deletion, and full index purge controls.
- Added memory operations to the dashboard: create, list/filter, semantic search, update, and delete flows against the live PostgreSQL memory store.
- Added plan/task operations to the dashboard: session-scoped plan listing, plan creation with initial tasks, plan status updates, task listing by plan, and task completion actions.
- Added in-dashboard performance graphing by plotting runtime query latency, indexing throughput, and CPU usage from sampled telemetry points.

## [0.3.10] - 2026-02-28

- Fixed Home Assistant dashboard 500 errors caused by ambiguous Blazor page route matching by consolidating dashboard routing to a single catch-all non-file route.
- Fixed dashboard rendering failures by adding missing anti-forgery middleware and catch-all route parameter binding required by interactive server rendering.

## [0.3.9] - 2026-02-28

- Fixed Home Assistant ingress 404 behavior for the Blazor dashboard by making the app base href path-base aware (`<base href="~/">`).
- Expanded dashboard route coverage in Blazor (`/`, `/dashboard`, and non-file catch-all) so ingress slug URLs resolve to the dashboard page instead of returning 404.

## [0.3.8] - 2026-02-28

- Added a native CLI database migration command `clone-db` (`src/NebulaRAG.Cli/Program.cs`) to clone one PostgreSQL database into another and verify key table counts (`rag_documents`, `rag_chunks`, `memories`, `plans`, `tasks`, `plan_history`, `task_history`).
- Finalized repository cleanup for the Blazor dashboard migration by removing the legacy `dashboard/` tree and related ignore/documentation references, leaving AddonHost Blazor as the single dashboard implementation.

## [0.3.7] - 2026-02-28

- Switched default database targets from `nebularag`/`brewmind` to `nebula` across runtime defaults and templates (`compose.yaml`, `container/ragsettings.container.json`, `src/NebulaRAG.Cli/ragsettings.json`, `.env.example`, and add-on `config.json`).
- Migrated the add-on dashboard runtime to an in-process .NET Blazor dashboard in `src/NebulaRAG.AddonHost/Components/**` and removed Node/NPM build dependency from the add-on Docker build pipeline.
- Added project-first dashboard aggregation endpoint `GET /api/dashboard/projects` that returns `projects -> plans/rag/memory` slices, backed by new per-project PostgreSQL aggregates in RAG and plan stores.
- Removed the legacy React/Vite `dashboard/` workspace folder and related ignore patterns now that the Blazor dashboard is the single supported dashboard implementation.

## [0.3.6] - 2026-02-27

- Made MCP plan-by-id operations session-agnostic in execution handlers (`get_plan`, `update_plan`, `complete_task`, `update_task`, `archive_plan`) to prevent unnecessary "plan does not belong to provided sessionId" failures.
- Updated MCP tool input schemas so `sessionId` is optional for plan-by-id operations and used as an audit metadata override when supplied.

## [0.3.5] - 2026-02-27

- Removed the "only one active plan per session" rule from plan creation flow so multiple active plans can coexist in a session when needed.
- Removed obsolete validator/exception artifacts tied to the old active-plan constraint.
- Kept lifecycle transition and history-tracking behavior intact for status/task updates.

## [0.3.4] - 2026-02-27

- Expanded plan lifecycle behavior in core MCP transport so `update_plan` can apply valid lifecycle statuses (`draft`, `active`, `completed`, `archived`) instead of archive-only status updates.
- Added generic plan status transition persistence (`UpdatePlanStatusAsync`) with history tracking in `PostgresPlanStore` and validated transitions in `PlanService`.
- Updated task-completion validation to allow completing tasks from `Pending` as well as `InProgress` for direct MCP `complete_task` execution flow.
- Updated project instruction files to standardize Nebula plan usage for multi-step work across:
	- `nebula-rag`,
	- `dot-claw`,
	- `dot-claw-setup-test`.

## [0.3.3] - 2026-02-27

- Refactored plan MCP integration to use the shared `NebulaRAG.Core` transport path directly (`McpTransportHandler`) instead of disconnected generated wrappers.
- Implemented executable handlers for all advertised plan tools (`create_plan`, `get_plan`, `list_plans`, `update_plan`, `complete_task`, `update_task`, `archive_plan`) with:
	- session ownership checks,
	- numeric id validation (accepting numeric strings safely),
	- plan/task JSON shaping for structured responses,
	- one-time plan schema initialization guard.
- Removed stale generated artifacts that drifted from current core contracts:
	- `src/NebulaRAG.Mcp/Services/PlanMcpTool.cs`,
	- `src/NebulaRAG.Mcp/Services/SessionValidator.cs`,
	- outdated MCP plan tests that targeted removed wrappers.
- Normalized MCP tool input schemas for plan/task ids to integer types to match storage/service contracts and reduce runtime conversion issues.

## [0.3.2] - 2026-02-27

- Fixed `CS1519` build failure in `src/NebulaRAG.Core/Mcp/McpTransportHandler.cs` caused by duplicated helper methods and an extra closing brace appended outside the class.
- Removed duplicate `SessionValidation` nested type declarations in `src/NebulaRAG.Core/Exceptions/PlanException.cs` that caused `CS0102` duplicate-definition failures.
- Resolved `TaskStatus` ambiguity in `src/NebulaRAG.Core/Services/PlanValidator.cs` by using an explicit alias for `NebulaRAG.Core.Models.TaskStatus`.
- Stabilized solution build by excluding non-integrated plan MCP service sources and related stale tests from compilation until service contracts are aligned.

## [0.3.1] - 2026-02-27

- **Fix / Revert**: Reverted a dashboard version bump to keep the add-on manifest focused on the add-on versioning and deploy stability (commit ab3a02e).
- **Packaging**: Clarified add-on version entries after several version bump attempts in the repo (commits 9a2e1db, c5db1fe).
- **Plans & Storage (Postgres)**: Added core Plan lifecycle storage and schema work for plans and tasks:
	- Plan CRUD operations and Task CRUD operations (commits 6562ff5, f0e7efd)
	- PostgreSQL schema initialization for plan storage (commit bd66f12)
	- History query and aggregated query support for PostgresPlanStore (commits ad66713, 97a1cf3)
	- Added `PlanNotFoundException` and domain models for plan lifecycle (commits 479b69b, 25fff7c)
- **Docs**: Completed storage layer CRUD operations plan documentation (commit ff12240)

## [0.3.0] - 2026-02-27

- **MCP Integration**: Added comprehensive Model Context Protocol (MCP) tools for plan lifecycle management.
	- 7 MCP tool handlers: create_plan, get_plan, list_plans, update_plan, complete_task, update_task, archive_plan.
	- Session validation middleware with ownership enforcement.
	- One active plan per session constraint.
	- Robust error handling and business rule validation.
- **Service Layer**: Implemented core business logic for plan and task management.
- **Testing**: Added 31 comprehensive tests covering all MCP operations and session validation.
- **Documentation**: Updated MCP integration guide and usage examples.

## [0.2.52] - 2026-02-26

- Updated repository guidance references in `AGENTS.md` to consistently point to `.editorconfig` (dotfile name) instead of `editorconfig`.

## [0.2.51] - 2026-02-26

- Fixed an MCP `memory_recall` failure where some result sets could surface `Invalid cast from 'DateTime' to 'Double'` during score parsing.
- Hardened semantic-score extraction in PostgreSQL search paths by resolving score columns by alias and falling back safely when provider values are non-numeric.
## [0.2.50] - 2026-02-24

- Added a targeted source-key remediation path to the CLI (`repair-source-prefix`) to rewrite legacy indexed source prefixes and resolve duplicate collisions safely.
- Patched source project-id extraction heuristics so namespace-prefixed `workspace-notes` and drive-style (`c:`) source keys remain grouped under their explicit top-level project identifier.
- Fixed memory analytics aggregation where optional session filtering in stats queries incorrectly generated a new session id, causing `/api/memory/stats` to return zero despite existing memories.

## [0.2.49] - 2026-02-24

- Unified MCP project scoping terminology to `projectId` for indexing tools (`rag_index_path`, `rag_index_text`, `rag_index_url`, `rag_reindex_source`) to match memory tools.
- Updated indexing tool input schemas and structured outputs to use `projectId` only, removing mixed `projectName` usage.
- Aligned core indexing/path-prefix method signatures with `projectId` naming for consistent project-scoping semantics across backend services.

## [0.2.48] - 2026-02-24

- Added `projectId` to source API payloads and dashboard source typing so source grouping/filtering can use explicit project identity when available.
- Updated source grouping logic to prefer explicit `projectId` first and only fall back to source-path extraction when `projectId` is absent.
- Added dashboard data-test fixture coverage for project-id precedence on workspace-prefixed source keys.

## [0.2.47] - 2026-02-24

- Fixed dashboard project extraction for workspace-prefixed source keys so paths like `Accentry.MiEP/NebulaRAG/src/...` are grouped under `NebulaRAG` instead of the outer workspace segment.
- Added Playwright data coverage to assert source grouping displays `NebulaRAG` and does not regress to `Accentry.MiEP` for prefixed source paths.

## [0.2.46] - 2026-02-24

- Improved Memory tab scope visibility by adding a dedicated `Applied Scope` status badge, making it obvious when analytics are in `Global` mode versus `Project`/`Session` filters.
- Kept global-all-memories as the default applied view, with scope changes only taking effect when users click `Apply Scope`.

## [0.2.45] - 2026-02-24

- Restored project-aware source views in the dashboard (`Source Breakdown` and `Source Management` project filter) using a generic project-key extraction strategy without hardcoded segment lists like `workspace-notes`, `notes`, or `docs`.
- Updated index stats project counting to compute distinct project keys from stored source-path prefixes/URLs, so the top KPI reports projects again.
- Fixed memory-scope UX so global memory analytics remain the default overall view; project/session filtering only applies after clicking `Apply Scope`.

## [0.2.44] - 2026-02-24

- Fixed stale `Indexing Rate (docs/sec)` values in the performance timeline by expiring old explicit indexing samples and combining them with live document-delta throughput.
- Updated telemetry sampling so indexing throughput decays back to zero when indexing is idle instead of remaining pinned from historical non-zero averages.

## [0.2.43] - 2026-02-24

- Removed dashboard snapshot source limiting: `/api/dashboard` now returns the full indexed source set instead of truncating by a limit parameter.
- Removed backend source-path project inference from index stats; the status counter now reflects distinct indexed source paths without heuristic grouping logic.
- Simplified dashboard source views to operate on direct source data (no project-name extraction/grouping rules in source breakdown and source manager).

## [0.2.42] - 2026-02-24

- Added scope-aware memory REST APIs in AddonHost: `GET /api/memory/stats`, `GET /api/memory/list`, and `POST /api/memory/search` now support `global`, `project`, and `session` filters with backward-compatible global defaults.
- Extended core management/storage flows to apply optional `sessionId`/`projectId` filters to memory analytics, list retrieval, and semantic memory search.
- Added dashboard memory scope controls (Global/Project/Session) so operators can switch memory insights to scoped views without losing the default global behavior.

## [0.2.41] - 2026-02-24

- Added additive project-scoping groundwork for memories in PostgreSQL storage by introducing optional `project_id`, index support, and backward-compatible memory store/list/recall overloads.
- Expanded MCP memory tool input contracts (`memory_store`, `memory_recall`, `memory_list`) with optional `projectId`, and propagated project scope through tool execution and structured outputs.
- Added dashboard memory visibility for project scope with a new `Distinct Projects` KPI in `Memory Insights` and updated dashboard fixtures/types accordingly.

## [0.2.40] - 2026-02-24

- Updated the README one-line installer to a PowerShell-native command intended for users already running PowerShell, avoiding nested `pwsh -Command` quoting pitfalls.
- Added stale temp-script cleanup and explicit download failure handling in the one-liner (`Remove-Item` + `Invoke-WebRequest -ErrorAction Stop`).

## [0.2.37] - 2026-02-24

- Cleaned invalid indexed workspace-note sources that surfaced as separate dashboard projects (`operations` and `retrieval-probe.md`).
- Updated `Memory Insights` chart layout so full-width screens render `Type Distribution` at one-third width and `Top Tags` at two-thirds width, while preserving stacked behavior on smaller breakpoints.

## [0.2.39] - 2026-02-24

- Expanded MCP `tools/list` input schemas to cover previously under-specified tools: `rag_list_sources`, `rag_normalize_source_paths`, `rag_delete_source`, `rag_purge_all`, `memory_delete`, and `memory_update`.
- Added stronger schema metadata (enum/const/boolean/integer constraints) so MCP clients can generate safer calls with fewer trial-and-error failures.
- Improved `rag_list_sources` usability with optional `limit` input and richer response metadata (`returnedCount`, `totalCount`, `limit`) for better paging/inspection workflows.

## [0.2.37] - 2026-02-24

- Fixed MCP tool discoverability for `rag_get_chunk` by publishing an explicit input schema with required `chunkId` so clients stop issuing empty argument calls.
- Added explicit input schemas for `query_project_rag` and `rag_search_similar` to improve argument guidance in `tools/list`.
- Improved `rag_get_chunk` argument handling to accept numeric-string values and return a clearer validation message with a usage example when `chunkId` is missing/invalid.

## [0.2.36] - 2026-02-24

- Fixed remote `setup-nebula-rag.ps1` behavior when launched from a downloaded temp script by adding template-file fallback downloads from GitHub raw for required setup assets (`AGENTS.md`, `.github/copilot-instructions.md`, `.github/instructions/rag.instructions.md`, and skill templates).
- Added a configurable installer parameter `-TemplateRawBaseUrl` (defaulting to the NebulaRAG `main` raw URL) used to resolve missing template files in user/project setup paths.
- Updated env-template and global/project scaffold flows to use the shared template resolver so setup no longer fails with local "Source file not found" errors in remote-only installs.

## [0.2.35] - 2026-02-24

- Updated MCP `memory_store` behavior so `sessionId` is optional; when omitted, NebulaRAG now generates a session identifier automatically.
- Removed store-level hard validation that rejected empty `sessionId` during memory creation, while preserving required `type` and `content` validation.
- Added explicit `memory_store` input schema in MCP `tools/list` so clients can discover required fields (`type`, `content`) and optional fields (`sessionId`, `tags`).

## [0.2.34] - 2026-02-24

- Updated the README one-line PowerShell installer to run `setup-nebula-rag.ps1` without hardcoded setup arguments, so the script uses its own defaults/prompts.

## [0.2.32] - 2026-02-24

- Simplified the README one-line PowerShell installer flow to only download `setup-nebula-rag.ps1` and execute it.
- Kept the quoting-safe `-Command` pattern so the command works reliably when launched from an existing PowerShell session.

## [0.2.31] - 2026-02-24

- Fixed the README remote one-line installer command for PowerShell sessions by switching to quoting-safe `-Command` usage that prevents parent-session `$variable` expansion.
- Hardened the one-line installer flow with download retry behavior and `try/finally` cleanup of the temporary script file.

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
