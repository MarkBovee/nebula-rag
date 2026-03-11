# OpenPencil Workflow Reference

## Purpose

Use this reference when performing actual OpenPencil design work in a repository workflow.

## Source Of Truth Order

1. Visible OpenPencil browser page if one is open.
2. Real local upstream OpenPencil repo/editor if available.
3. Repo scripts and saved `.fig` assets.
4. Older notes and prompts.

The browser page wins when the live page and the automation bridge drift apart.

## Standard Execution Order

1. Query project memory for prior OpenPencil decisions and runtime gotchas.
2. Run one focused `rag_query` for OpenPencil workflow context.
3. Check whether a visible page is already open.
4. If needed, use `scripts/openpencil/install-openpencil.ps1`, `start-openpencil-mcp.ps1`, or `start-openpencil-live-loop.ps1`.
5. Build or refine the design directly in the live editor.
6. Save to `designs/openpencil/*.fig`.
7. Update release metadata files when required by the host repository policy.
8. Validate touched files and changed-file state.

## Known Repo Conventions

- `.fig` files are kept flat in `designs/openpencil/`.
- The OpenPencil MCP endpoint is `http://localhost:3100/mcp`.
- `http://localhost:3100/` returning `404` is expected.
- The workflow is browser-first and PowerShell-driven.
- Bun must already be installed before using the install script.

## Reliable Save Strategy

Use browser-native save only when it works cleanly.

Preferred fallback:

1. Export `.fig` bytes from the running page.
2. Decode the base64 payload.
3. Write the result directly to the target file in `designs/openpencil/`.

This fallback is more reliable in this environment than file chooser dialogs.

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

## Design Session Pattern

Use named, reusable blocks instead of anonymous shapes whenever the work is intended as a skill or baseline.

Examples of good naming:

- `Overview`
- `Pattern Board`
- `States Card`
- `Action Rail`
- `Signal Dock`

## Validation Checklist

- Target `.fig` file exists under `designs/openpencil/`.
- Any related metadata changes are reflected in the repository release metadata files.
- Touched text files show no editor errors.
- If the work came from a live page, key nodes or sections can be re-identified after the save.

## When To Stop

Stop when the user asked for a saved or refined design and all of these are true:

- the `.fig` exists,
- the changed repo files are valid,
- and the reusable outcome has been summarized clearly.