# OpenPencil Workflow Reference

## Purpose

Use this reference when performing actual OpenPencil design work in a repository workflow.

## Source Of Truth Order

1. Visible OpenPencil browser page if one is open.
2. Running upstream OpenPencil runtime if available.
3. Repo scripts and saved `.fig` assets.
4. Older notes and prompts.

The browser page wins when the live page and the automation bridge drift apart.

## Standard Execution Order

1. Query project memory for prior OpenPencil decisions and runtime gotchas.
2. Run one focused `rag_query` for OpenPencil workflow context.
3. If the task is about visual quality, theme, or layout refinement, apply the `frontend-design` skill guidance before editing the canvas.
4. Check whether a visible page is already open.
5. If needed, start or reuse the upstream OpenPencil runtime and use `.github/skills/openpencil-design/scripts/start-openpencil-live-loop.ps1` for MCP-backed live variant reloads.
6. Build or refine the design directly in the live editor.
7. Save to `designs/*.fig`.
8. Update release metadata files when required by the host repository policy.
9. Validate touched files and changed-file state.

## Known Repo Conventions

- `.fig` files are kept flat in `designs/`.
- The workflow is browser-first and PowerShell-driven.
- The upstream local OpenPencil editor typically runs on `http://localhost:1420`.
- The upstream local OpenPencil MCP endpoint typically runs on `http://localhost:3100/mcp`.
- The upstream MCP file route for browser reloads is typically `http://localhost:3100/file?path=designs/<name>.fig` when the runtime root points at this repo workspace.
- Prefer `.github/skills/openpencil-design/scripts/openpencil-browser-automation.js` and `window.__OPEN_PENCIL_STORE__` as the browser automation entry point.

## Reliable Save Strategy

Use browser-native save only when it works cleanly.

Preferred fallback:

1. Export `.fig` bytes from the running page.
2. Decode the base64 payload.
3. Write the result directly to the target file in `designs/`.

This fallback is more reliable in this environment than file chooser dialogs.

Avoid large inline shell literals when writing exported base64. Prefer decoding from a captured payload file or other non-truncating path.

## Live Runtime Gotchas

### `selectedIds` must stay a `Set`

When manipulating the OpenPencil editor store, `selectedIds` must remain a `Set`.

Do not assign a plain object or array-like placeholder.

Wrong state causes errors like:

- `selectedIds is not iterable`
- `ids is not iterable`

If this happens, repair the store by restoring `selectedIds` to a `Set` and request a render.

### Save dialog reliability

Do not depend on browser save dialogs in this environment. Prefer export-to-bytes and direct write.

### Headless export caveat

Headless export paths may fail with `window is not defined`. Treat the visible editor workflow as the stable route when that happens.

### Live canvas drift after export

The visible page can drift away from the saved artifact after scripted generation or export.

Observed failure modes include:

- the document name reverting to `Untitled`,
- the page label reverting to `Page 1`,
- the canvas appearing blank,
- and top-level frames no longer being present in the live graph even though the `.fig` archive was saved.

When this happens:

1. Treat the saved `.fig` file as the durable artifact.
2. Inspect the live graph for expected named top-level frames before trusting the visible page.
3. Re-open the saved file if the page state no longer matches the saved result.
4. Prefer the live-loop watcher because it now validates the `.fig` archive and reopens the editor URL on variant changes through the MCP-backed file route when available.

### Scripted store access brittleness

Do not assume the OpenPencil store is reachable only from a Vue component path.

Prefer `window.__OPEN_PENCIL_STORE__` first. In the local upstream OpenPencil repo, the active tab wiring already publishes the active store there.

If that value is missing, use `.github/skills/openpencil-design/scripts/openpencil-browser-automation.js` as the fallback discovery layer instead of open-coding new Vue tree probes in each session.

### In-page module resolution caveat

Do not rely on ad-hoc bare-module imports inside page-evaluated snippets when store access or scene summaries are all that is needed.

Use `.github/skills/openpencil-design/scripts/openpencil-browser-automation.js` first so routine automation stays on the already-loaded runtime surface instead of per-session module hacks.

### Render notification after scripted generation

Scripted node generation may trigger notifications such as `Cannot read properties of undefined (reading 'x')`.

If that appears:

1. Inspect the graph for malformed nodes and missing parents.
2. Confirm `selectedIds` is still a `Set`.
3. Validate the exported `.fig` archive before continuing.
4. Re-open the saved file before declaring the live canvas healthy.

## Observed Session Issues

These issues were observed while generating the Nebula server dashboard skill artifact and should be treated as recurring skill-level risks until OpenPencil or the workflow proves otherwise:

- The live page later showed a blank `Untitled` canvas even though a valid `.fig` archive had already been produced.
- A runtime notification reported `Cannot read properties of undefined (reading 'x')` after scripted generation.
- The first attempt to access the editor store hit the wrong Vue level and returned no store/graph.
- Bare `@open-pencil/core` imports failed inside page eval and required an explicit `/@fs/...` module path.
- Browser file-save dialogs were not reliable enough for the primary workflow.
- Large inline terminal writes are risky for exported base64 payloads and can lead to corrupted output if the payload handling path is wrong.

## Skill Improvement Plan

1. Add a mandatory post-export verification step that checks expected top-level frame names in the live graph and re-opens the saved `.fig` when the visible page drifts.
2. Add a troubleshooting branch to the skill for blank-canvas recovery, including document-name drift (`Untitled`) and page-reset detection.
3. Add reusable helper guidance for discovering `EditorView.setupState.store` instead of assuming a fixed Vue entry point.
4. Add explicit module-resolution guidance for page-evaluated scripts so export helpers do not rely on failing bare imports.
5. Strengthen artifact-writing guidance to avoid large inline shell literals and prefer captured-payload decode paths.
6. Extend validation from file existence to archive readability plus correlation to expected semantic frame names.
7. Capture runtime notifications as part of the design-session summary so future skill runs can distinguish OpenPencil runtime faults from workflow faults.

## Design Session Pattern

Use named, reusable blocks instead of anonymous shapes whenever the work is intended as a skill or baseline.

Examples of good naming:

- `Overview`
- `Pattern Board`
- `States Card`
- `Action Rail`
- `Signal Dock`

## Validation Checklist

- Target `.fig` file exists under `designs/`.
- The `.fig` archive can be opened and contains expected entries such as `canvas.fig`, `thumbnail.png`, and `meta.json`.
- The live-loop watcher or equivalent flow reopens the current editor URL after a validated `.fig` update when live-canvas drift recovery matters.
- Any related metadata changes are reflected in the repository release metadata files.
- Touched text files show no editor errors.
- If the work came from a live page, key nodes or sections can be re-identified after the save or after re-opening the exported file.

## When To Stop

Stop when the user asked for a saved or refined design and all of these are true:

- the `.fig` exists,
- the changed repo files are valid,
- and the reusable outcome has been summarized clearly.