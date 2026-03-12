# Changelog

All notable changes to the Nebula RAG Home Assistant add-on are documented in this file.

The format is inspired by Keep a Changelog and follows semantic versioning.

## [0.3.60] - 2026-03-12

- Removed the repo-owned `.github/skills/openpencil-design/` tree so NebulaRAG no longer carries the OpenPencil project skill as local source.
- Cleaned the Nebula OpenPencil docs to treat the project-local live-loop helpers as an optional install from an OpenPencil checkout instead of a repo-owned asset.
- Bumped the OpenPencil workflow docs to point at the new OpenPencil-root installer for downstream projects such as NebulaRAG.

## [0.3.59] - 2026-03-12

- Extended `.github/skills/openpencil-design/` with the newer live-run-first OpenPencil guidance so the skill now explicitly covers AI-tab run visibility, snapshot validation, invisible-node troubleshooting, and viewport-focus recovery.
- Added `.github/skills/openpencil-design/scripts/install-openpencil-design-skill.ps1` to install the complete OpenPencil skill into the user-wide agent skill directory (`~/.agents/skills/openpencil-design` by default).
- Updated the repository README with the new one-command OpenPencil skill installation path.

## [0.3.58] - 2026-03-12

- Fixed MCP plan task status persistence so `update_task(status=in_progress)` uses the PostgreSQL-compatible `in_progress` value instead of the broken `inprogress` form.
- Routed MCP `update_task(status=completed)` through the dedicated task completion flow so completion requests no longer fail with a misleading pending-to-completed transition error.
- Blocked plan completion until all plan tasks are terminal, preventing MCP clients from marking a plan complete while tasks are still pending or in progress.
- Refreshed merge-gate dependencies by applying the current NuGet patch updates and updating the vendored TailAdmin template runtime package ranges to their latest stable versions.

## [0.3.57] - 2026-03-11

- Added `docs/openpencil-runtime-workflow.md` to document how the private OpenPencil mirror, Nebula submodule, runtime startup, live-loop flow, and submodule update process work together.
- Linked the new OpenPencil runtime workflow guide from the README so the update process is documented in one place instead of being implicit in recent git history.

## [0.3.56] - 2026-03-11

- Added the private `open-pencil` mirror as a git submodule at the repository root so Nebula clones can build or run the upstream OpenPencil container/runtime without a separate manual clone step.
- Updated the OpenPencil integration docs to reflect the in-repo submodule and the flat `designs/*.fig` storage convention in the current project structure.

## [0.3.55] - 2026-03-11

- Added a ready-to-use `open-pencil` entry to `.mcp.json` so local editor tooling can attach directly to `http://localhost:3100/mcp` alongside the Nebula MCP server.
- Switched the OpenPencil live-loop to prefer an MCP-backed file URL for `designs/*.fig`, which makes the same reload flow work with containerized OpenPencil when its MCP root points at this repository workspace.
- Documented `OPENPENCIL_MCP_URL` and the runtime file-route contract so the browser-first design loop no longer depends on a local `public/` mirror alone.

## [0.3.54] - 2026-03-11

- Updated the OpenPencil integration guidance to assume the upstream runtime can be local or containerized while keeping the integration contract centered on the editor URL `http://localhost:1420` and MCP URL `http://localhost:3100/mcp`.
- Adjusted the OpenPencil skill wording to prefer the running upstream runtime over local checkout path references, making later MCP-server integration cleaner.

## [0.3.53] - 2026-03-11

- Added a simple local OpenPencil startup script in the upstream OpenPencil checkout so the editor and upstream MCP can be started together with the expected default URLs `http://localhost:1420` and `http://localhost:3100/mcp`.
- Cleaned the NebulaRAG OpenPencil guidance so it refers to the running editor and MCP URLs instead of local folder paths or wrapper-runtime terminology.

## [0.3.52] - 2026-03-11

- Removed the repo-owned OpenPencil MCP wrapper scripts and container assets so this repository no longer suggests a second MCP runtime alongside upstream OpenPencil.
- Simplified the OpenPencil skill docs and live-loop usage to assume the upstream OpenPencil editor and its built-in MCP support instead of `start-openpencil-mcp.ps1`, Podman flags, or local wrapper endpoints.

## [0.3.51] - 2026-03-11

- Removed the accidentally reintroduced nested `designs/openpencil/nebula-server-dashboard.fig` artifact so the OpenPencil design storage stays flat under `designs/*.fig`.

## [0.3.50] - 2026-03-11

- Hardened the OpenPencil live-loop to validate `.fig` archives before sync and reopen the editor URL when the latest design changes, reducing blank-canvas drift after scripted updates.
- Added a stable repo-owned browser automation helper for OpenPencil so sessions can use `window.__OPEN_PENCIL_STORE__` and a shared fallback helper instead of reintroducing brittle Vue-tree probing and ad-hoc module hacks.

## [0.3.49] - 2026-03-11

- Added GitHub follow-up issues #24 and #25 to track the OpenPencil MCP blank-canvas recovery gap and the need for stable automation hooks for editor-store and module access.
- Updated the OpenPencil skill workflow to explicitly apply the `frontend-design` skill guidance when design quality, theming, typography, or composition refinement is part of the request.

## [0.3.48] - 2026-03-11

- Moved the repo-owned OpenPencil helper implementation out of `scripts/openpencil/` and into `.github/skills/openpencil-design/scripts/` so the skill now carries its own runnable install, MCP, live-loop, stop, check, and container build assets.
- Updated the OpenPencil skill docs and README commands to use the skill-local script entrypoints instead of the old top-level scripts folder.

## [0.3.47] - 2026-03-11

- Hardened the OpenPencil live workflow so the watcher mirrors the latest `designs/*.fig` file into the sibling local editor `public/` folder and opens the browser with an `?open=/file.fig&fit=1` URL.
- Added OpenPencil startup restore and automatic fit-to-content so refreshes and reloads reopen the last mirrored design instead of falling back to an empty `Untitled` canvas.

## [0.3.46] - 2026-03-11

- Improved readability in the saved Nebula dashboard design by shortening row and sidebar copy, tightening labels, and reducing text overflow in the live OpenPencil review.
- Kept the calmer layout from the earlier cleanup pass while making the visible project, KPI, memory, and watchlist text fit more cleanly on the canvas.

## [0.3.45] - 2026-03-11

- Tightened the live Nebula dashboard cleanup pass with calmer hero spacing, fewer sidebar and table distractions, and a simpler operator panel.
- Reduced visible noise in the saved Nebula-themed design so the main project, memory, and retrieval surfaces are easier to scan while reviewing in OpenPencil.

## [0.3.44] - 2026-03-11

- Flattened repository design storage to `designs/*.fig` and updated the OpenPencil live-loop default plus workflow references to match.
- Refined the live Nebula dashboard direction toward a cleaner, less noisy composition with a stronger Nebula visual identity and tighter typography targets.

## [0.3.43] - 2026-03-11

- Added a Nebula-themed OpenPencil variant at `designs/nebula-server-dashboard-nebula-theme.fig` with a darker stage, brighter operator surfaces, and atmospheric glow accents.
- Refined the live dashboard composition with stronger visual hierarchy across the project rail, hero, KPI tiles, action buttons, states, and reusable pattern board.
- Kept the dashboard grounded in real NebulaRAG project hierarchy data while adding a more distinctive visual identity.

## [0.3.42] - 2026-03-11

- Added a grounded OpenPencil dashboard artifact at `designs/nebula-server-dashboard-project-data.fig` using live NebulaRAG project hierarchy data from `GET /api/dashboard/projects` and `dotnet run --project src\\NebulaRAG.Cli -- stats`.
- Reworked the visible dashboard content around real project IDs and counts, including project nodes, plans, tasks, RAG documents/chunks/tokens, and memory totals.
- Aligned the local OpenPencil workflow env setting by pointing `OPENPENCIL_EDITOR_URL` at the active local editor and dropping the unused Podman image override when Podman is disabled.

## [0.3.41] - 2026-03-11

- Documented observed OpenPencil skill failure modes in `.github/skills/openpencil-design/`, including live-canvas drift to blank `Untitled` state, brittle store discovery, render notifications, and page-eval module-resolution issues.
- Added a concrete improvement plan to the OpenPencil workflow reference for post-export verification, blank-canvas recovery, safer artifact writing, and stronger archive validation.
- Tightened OpenPencil skill guardrails and quality gates so saved-artifact validation no longer relies on file existence alone.

## [0.3.40] - 2026-03-11

- Added a new OpenPencil dashboard design artifact at `designs/nebula-server-dashboard.fig` for Nebula server management.
- Structured the dashboard around project-based navigation with a persistent project switcher, within-project sections, and operational health surfaces.
- Included reusable pattern-board blocks and explicit loading, empty, error, and success state cards to support later implementation handoff.

## [0.3.39] - 2026-03-11

- Made `.github/skills/openpencil-design/SKILL.md` project-independent by removing NebulaRAG-specific references and replacing repository-coupled wording with generic guidance.
- Kept design storage guidance centered on `designs/*.fig` and explicitly Figma-compatible `.fig` outputs.
- Updated OpenPencil skill references (`references/workflow.md`, `references/prompts.md`) to remove remaining Nebula-specific language and keep reusable cross-project wording.

## [0.3.38] - 2026-03-11

- Hardened `.github/skills/openpencil-design/SKILL.md` for production usage with clearer activation heuristics, explicit decision points, and concrete quality gates.
- Fixed the skill workflow sequence to include the missing first step (`memory` + one focused `rag_query`) and aligned execution language with current OpenPencil browser-first conventions.
- Standardized skill path guidance to `designs/*.fig` so required outputs, defaults, and guardrails are consistent with repo OpenPencil workflow references.

## [0.3.37] - 2026-03-11

- Broadened the OpenPencil repo skill from dashboard-only work to general UI and design work, including generic triggers such as creating or refining a design.
- Added the generalized repo skill at `.github/skills/openpencil-design/` with updated workflow, prompts, and handoff guidance for reusable UI work.
- Updated `README.md` to point to the generalized OpenPencil design skill.

## [0.3.36] - 2026-03-11

- Added the new repo skill at `.github/skills/openpencil-dashboard/` for OpenPencil-first dashboard design, live canvas refinement, reliable `.fig` saving, and implementation handoff.
- Added compact OpenPencil skill references for workflow, prompts, and handoff guidance so later dashboard sessions can reuse the same execution model.
- Updated `README.md` to make the new OpenPencil repo skill discoverable from the existing design workflow section.

## [0.3.35] - 2026-03-11

- Added the saved live OpenPencil dashboard skill asset at `designs/nebula-live-skill-v1.fig`.
- Extended the local OpenPencil design session into a more reusable skill baseline with pattern-library blocks and a refined overview composition for later dashboard build handoff.

## [0.3.34] - 2026-03-11

- Removed the remaining Windows-specific OpenPencil setup assumptions from `scripts/openpencil/install-openpencil.ps1` so the repo now requires a preinstalled Bun runtime instead of calling `winget`.
- Made `scripts/openpencil/openpencil-common.ps1` and `scripts/openpencil/start-openpencil-mcp.ps1` portable by removing Windows-only process-window behavior and adding non-Windows MCP process detection.
- Added a default Podman image fallback (`nebula-openpencil-mcp:latest`) so `scripts/openpencil/start-openpencil-mcp.ps1 -UsePodman` works immediately after the repo-owned image is built, even without a `.env` file.
- Updated the OpenPencil docs to describe PowerShell-based, browser-first setup without Windows-specific installer guidance.
- Cleaned historical OpenPencil changelog wording so it no longer points at removed repo paths or old desktop-oriented implementation details.

## [0.3.33] - 2026-03-11

- Added a repo-owned OpenPencil MCP container path via `scripts/openpencil/Containerfile` and `scripts/openpencil/build-openpencil-mcp-image.ps1`, making the Podman image flow reproducible inside this repository.
- Added `.env`-driven OpenPencil settings (`OPENPENCIL_EDITOR_URL`, `OPENPENCIL_USE_PODMAN`, `OPENPENCIL_MCP_PODMAN_IMAGE`) and updated the MCP/live-loop scripts to use them when parameters are omitted.
- Extended `scripts/openpencil/stop-openpencil-mcp.ps1` to stop both local MCP processes and the named Podman container.
- Updated `README.md` and `docs/OpenPencil-Dashboard-Plan.md` for the repo-owned container flow and browser-first configuration model.
- Clarified the older `0.3.30` OpenPencil entry as an initial loop implementation that has since been superseded by the browser-first flow.

## [0.3.31] - 2026-03-11

- Flattened OpenPencil design storage to `designs/*.fig` so the design folder now keeps only the generated `.fig` files.
- Removed repo-stored OpenPencil artifact subfolders under `designs` (`runs`, `exports`, and `viewer`) as part of the simpler design handoff flow.
- Updated `scripts/openpencil/start-openpencil-live-loop.ps1` and `docs/OpenPencil-Dashboard-Plan.md` to use the flat `designs` folder as the automatic watch/open target.

## [0.3.32] - 2026-03-11

- Removed the Windows desktop `.exe` dependency from the OpenPencil live-loop flow so the repo no longer assumes a local desktop app.
- Updated `scripts/openpencil/start-openpencil-mcp.ps1` and `scripts/openpencil/start-openpencil-live-loop.ps1` for browser-first usage with optional `-OpenUiUrl` and optional Podman-backed MCP startup.
- Updated `docs/OpenPencil-Dashboard-Plan.md` to stop hardcoding `app.openpencil.dev/demo`, document browser/PWA + MCP separation, and describe Podman as the portable runtime option.

## [0.3.30] - 2026-03-11

- Added the first `scripts/openpencil/start-openpencil-live-loop.ps1` automation loop for local OpenPencil usage; this initial version was later superseded by the browser-first flow.
- Added optional MCP bootstrap in the same loop script via `-StartMcp` so the initial local OpenPencil flow and MCP readiness could be started in one command.
- Updated `docs/OpenPencil-Dashboard-Plan.md` with a concrete no-manual live loop command and one-shot fallback command.

## [0.3.29] - 2026-03-11

- Added a new OpenPencil-generated full dashboard variant (`dashboard-agent-v3.fig`), now kept in the flattened `designs/` folder after the later design-storage cleanup.
- Removed the temporary custom local design viewer fallback (`designs/viewer/**`) and related helper scripts (`scripts/openpencil/start-design-viewer.ps1`, `scripts/openpencil/stop-design-viewer.ps1`).
- Updated `docs/OpenPencil-Dashboard-Plan.md` to use standalone OpenPencil workflow as the primary route for creating a new dashboard design (no AddonHost tab integration).

## [0.3.28] - 2026-03-11

- Fixed `rag_ingest` path mode to support both directory and single-file input paths, including explicit `pathType`, `sourcePath`, and `resolvedPath` in responses.
- Improved `rag_ingest` path-mode validation errors to distinguish file-vs-directory missing path scenarios.
- Extended `plan` `update_task` status handling to support `pending`, `in_progress` (and `in-progress` alias), `completed`, and `failed` via validated task lifecycle transitions.
- Added `PostgresPlanStore.UpdateTaskStatusAsync` and `TaskService.UpdateTaskStatusAsync` to support audited non-completion task status transitions.
- Mitigated CodeQL `cs/log-forging` findings in AddonHost MCP request logging by sanitizing untrusted request metadata before logging.
- Added the new Nebula repository skill at `.github/skills/nebularag/SKILL.md` for unified RAG, memory, plan, and system workflow guidance.
- Updated `.github/instructions/rag.instructions.md` to use action-based memory persistence guidance (`memory` + `store`) instead of legacy `memory_store` naming.
- Updated `.github/copilot-instructions.md` to explicitly call out the consolidated Nebula MCP toolset (`rag_query`, `rag_ingest`, `rag_sources`, `rag_admin`, `memory`, `plan`, `system`).

## [0.3.27] - 2026-03-08

- Removed remaining "preferred" wording from MCP tool descriptions and documentation now that migration is complete.
- Updated MCP transport tool metadata text to consistently describe the unified tool surface.
- Updated README and add-on docs headings/phrasing from "Preferred" to "Unified".

## [0.3.27] - 2026-03-08

- Disabled automatic `rag-sources.md` sidecar synchronization in CLI and MCP mutation flows so indexing remains database-first without writing local manifest files.
- Removed the README statement that claimed `rag-sources.md` is automatically synchronized after index operations.

## [0.3.26] - 2026-03-08

- Ran dependency freshness checks against latest stable NuGet packages across the solution before merge readiness validation.
- Applied safe upgrades in AddonHost: OpenTelemetry packages (`Extensions.Hosting`, `Exporter.OpenTelemetryProtocol`, `Instrumentation.AspNetCore`, `Instrumentation.Http`, `Instrumentation.Runtime`) from `1.12.0` to `1.15.0`.
- Applied safe Serilog core upgrade in AddonHost from `4.1.0` to `4.3.1`.
- Upgraded `Serilog.AspNetCore` in AddonHost from `8.0.3` to `10.0.0` for .NET 10 alignment.
- Applied safe test infrastructure upgrade: `Microsoft.NET.Test.Sdk` from `18.0.1` to `18.3.0`.

## [0.3.25] - 2026-03-08

- Removed legacy MCP tool-name endpoints from runtime dispatch; only unified tool names are callable (`rag_query`, `rag_ingest`, `rag_sources`, `rag_admin`, `memory`, `plan`, `system`).
- Simplified `tools/list` to always return the minimal unified tool catalog and dropped legacy/full alias expansion behavior.
- Updated MCP contract tests to verify legacy tool calls are rejected as unknown tools.
- Updated project guidance files (`AGENTS.md`, `.github/copilot-instructions.md`, `.github/instructions/rag.instructions.md`) to use unified action-based tool format.
- Updated README and add-on docs to remove legacy alias guidance and show unified MCP call patterns.

## [0.3.24] - 2026-03-08

- Changed MCP `tools/list` default catalog profile to `minimal`, returning only preferred consolidated tools (`rag_query`, `rag_ingest`, `rag_sources`, `rag_admin`, `memory`, `plan`, `system`).
- Added MCP `tools/list` opt-in support for legacy aliases via `params.profile = "full"` or `params.includeLegacy = true`.
- Expanded MCP contract tests to validate both default minimal catalog behavior and explicit full-profile legacy catalog behavior.
- Updated README and add-on docs with the new tools/list profile behavior and corrected plan/session wording.

## [0.3.23] - 2026-03-08

- Added a consolidated preferred MCP tool surface in `McpTransportHandler`: `rag_query`, `rag_ingest`, `rag_sources`, `rag_admin`, `memory`, `plan`, and `system`.
- Kept legacy MCP tool names fully available as compatibility aliases so existing agent/tooling integrations do not break.
- Updated MCP transport contract coverage to assert the consolidated tool names are advertised via `tools/list`.
- Updated README and add-on docs to document preferred consolidated tools and clarify current session behavior for plan-by-id operations.

## [0.3.22] - 2026-03-08

- Reduced `NebulaRAG.Cli` console noise by lowering default structured logger verbosity and suppressing startup info messages that were polluting command output.
- Added a plain warning when no `.env` file is discovered so environment fallback behavior is explicit without JSON log spam.
- Extended `nebula stats` to include project-first grouping output (per project: document count, chunk count, token total, and newest index timestamp).
- Added `RagManagementService.GetProjectRagStatsAsync` to expose project-level RAG aggregates for CLI and operational surfaces.

## [0.3.21] - 2026-03-08

- Added shared startup `.env` loading in all executable hosts (`NebulaRAG.Cli`, `NebulaRAG.Mcp`, and `NebulaRAG.AddonHost`) so `NEBULARAG_*` variables are applied automatically from the nearest `.env` file.
- Standardized repository tooling and docs to `.env` only by removing `.nebula.env` references across setup script defaults, security/agent instruction files, and MCP container config.
- Removed the legacy `.nebula.env` file and switched setup-generated env paths/templates to `.env`.

## [0.3.20] - 2026-03-08

- Migrated solution entry usage from `NebulaRAG.sln` to `NebulaRAG.slnx` in CI and Docker build inputs (`.github/workflows/security.yml`, `Dockerfile`).
- Updated CLI preload project-marker detection to recognize `.slnx` files when auto-detecting candidate roots.
- Added guidance and support for a persistent PowerShell `nebula` terminal function alias that forwards to `src/NebulaRAG.Cli` for direct command usage.

## [0.3.19] - 2026-03-08

- Added a new CLI command `preload` in `src/NebulaRAG.Cli/Program.cs` to bootstrap project indexing without a manifest.
- Implemented automatic project-source detection with confidence scoring from the current working directory and first-level subfolders.
- Added interactive fallback prompts when detection is uncertain, allowing users to select a detected source, all detected sources, current directory, or a custom directory.
- Added `--dry-run` support for `preload` to preview selected preload paths without writing index changes.

## [0.3.18] - 2026-03-01

- Added a shared intake questioning skill at `.github/skills/intake-questioning/` with lightweight gray-area selection, four-question discussion loops, readiness gates, and scope guardrails for planning preparation.
- Added prompt command wrappers `.github/prompts/new-feature.prompt.md` and `.github/prompts/new-project.prompt.md` so new feature/project intake can be started with a consistent questioning flow.
- Added a reusable intake handoff template (`.github/skills/intake-questioning/templates/intake-output.md`) to standardize captured decisions, constraints, deferred ideas, and planning handoff notes.

## [0.3.17] - 2026-02-28

- Refactored dashboard Blazor components by splitting `Dashboard.razor` and `RagManagementTab.razor` into markup plus code-behind partial classes, reducing UI markup noise and clarifying component lifecycle/state intent.
- Added `RagOperationsService` in AddonHost to centralize reusable RAG tab operations (query, index, delete, purge, and source listing), reducing direct UI coupling to multiple core services.
- Extracted memory scope normalization/validation from `RagApiController` into a dedicated `MemoryScopeResolver` service and moved API request/response contracts into `RagApiContracts.cs` to reduce controller responsibility and improve maintainability.

## [0.3.16] - 2026-02-28

- Added MCP transport contract tests in `tests/NebulaRAG.Tests/McpTransportHandlerContractTests.cs` to validate JSON-RPC baseline behavior aligned with the MCP transports spec (`initialize`, `ping`, `tools/list`, notification handling, and protocol error codes for invalid/missing methods).
- Ensured transport regression coverage for method lookup and parameter validation paths that previously surfaced as handshake failures after dashboard changes.

## [0.3.15] - 2026-02-28

- Fixed MCP transport compatibility for Home Assistant ingress by making `/mcp` POST parsing tolerant and returning JSON-RPC parse errors with HTTP 200 instead of transport-level HTTP 400 responses.
- Added explicit `GET /mcp` handling to return a JSON-RPC method error with HTTP 405 so MCP fallback/probe requests no longer get routed to the dashboard shell.
- Added structured diagnostics for malformed/empty MCP POST payloads (content type, user agent, and bounded body preview) to speed up future handshake troubleshooting.

## [0.3.14] - 2026-02-28

- Fixed VS Code MCP `initialize` handshake failures (`400` on `/nebula/mcp`) by making the AddonHost `/mcp` endpoint accept both single JSON-RPC objects and batch JSON-RPC arrays.
- Disabled antiforgery validation specifically for the `/mcp` endpoint so non-browser MCP clients can call transport methods without CSRF tokens while preserving dashboard antiforgery protections.
- Added tolerant JSON parsing/validation for `/mcp` requests to return structured bad-request responses for malformed payloads instead of transport-level binding failures.

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
