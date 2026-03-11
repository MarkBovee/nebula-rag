---
name: openpencil-design
description: OpenPencil-first UI and product design skill. Use this when the user asks to create or refine screens, build reusable patterns, save or export .fig files, or translate live canvas work into implementation-ready handoff, even when OpenPencil is not named explicitly.
license: Complete terms in LICENSE.txt
---

# OpenPencil Design Skill

Use this skill when the task is to create or evolve UI designs in OpenPencil or a similar browser-first canvas tool.

This skill captures a browser-first workflow, reliable save/export behavior, and handoff expectations for reusable UI work.

When the request includes visual polish, theming, typography, composition, or overall UI quality, apply the `frontend-design` skill as the design-quality bar for the OpenPencil work instead of treating this as a purely mechanical export workflow.

## Use This Skill For

- OpenPencil UI design requests
- Creating or refining screens, views, flows, or reusable UI pattern libraries
- Working with Figma-compatible `.fig` files under `designs/`
- Live editing in the visible OpenPencil browser session
- Converting UI ideas into implementation-ready design handoff
- Saving or exporting the current OpenPencil canvas reliably
- Turning recurring OpenPencil work into a repeatable repo workflow

Trigger phrases include:

- "create a design"
- "maak een design"
- "refine this design"
- "design this screen"
- "save this .fig"
- "maak hier een skill van"
- "werk verder aan de pattern library"
- "design a ui"
- "turn this into reusable ui patterns"
- "OpenPencil export"
- "live canvas"

## Default Repo Assumptions

- Design assets live in `designs/*.fig`.
- OpenPencil helper scripts for this skill live under `.github/skills/openpencil-design/scripts/`.
- Browser automation helpers for this skill live under `.github/skills/openpencil-design/scripts/openpencil-browser-automation.js`.
- The local editor is typically browser-first, not desktop-app-first.
- If a visible OpenPencil browser page is open, treat that page as the source of truth for design state.
- Prefer a running upstream OpenPencil runtime at the expected local editor and MCP URLs over hosted shortcuts.
- Prefer a runtime whose MCP root points at the active repo workspace so the live-loop can reopen `designs/*.fig` through the MCP file route.

## Activation Heuristics

Use this skill by default when user intent includes one or more of these outcomes:

- Design creation or redesign
- UI refinement of an existing composition
- Saving/exporting live design work to a Figma-compatible `.fig`
- Extracting reusable components or pattern boards from a screen
- Design-to-implementation handoff preparation

If the user asks only for static concept prose and does not ask for artifacts, keep execution lightweight and confirm whether a `.fig` should still be produced.

## Required Outputs

For real design work, leave behind these concrete artifacts:

1. A saved Figma-compatible `.fig` asset in `designs/`.
2. A refined live canvas or a new UI composition.
3. Reusable pattern blocks when the request is broader than a one-off screen.
4. A concise build handoff summary when implementation follow-up is requested.

## Workflow

1. Recall relevant project context and run one focused context query when available.
2. Reuse existing OpenPencil docs, scripts, and prior design artifacts before inventing a new flow.
3. If the user expects a strong visual result, load and apply the `frontend-design` skill guidance before editing the canvas.
4. If a browser page is open, inspect the live page first.
5. Prefer editing the visible page or the running upstream OpenPencil runtime over abstract-only design descriptions.
6. Save work as a real Figma-compatible `.fig`, not only as prose or screenshots.
7. If save dialogs are unreliable, use the in-page export fallback described in `references/workflow.md`.
8. For substantial work, preserve reusable output as a named pattern board or baseline.
9. Validate touched files and keep related metadata aligned with the host repository policy.

If the visible page resets, goes blank, or drifts from the saved artifact, treat the saved `.fig` plus explicit validation as the source of truth until the file is re-opened successfully.

## Decision Points

- If a live canvas exists: continue from the visible page first.
- If no live canvas exists: start or reuse the upstream OpenPencil runtime and then continue with the local artifact/watch helpers.
- If the request asks for stronger styling, theme, typography, or composition: combine this skill with `frontend-design` guidance.
- If browser save works: use native save.
- If browser save fails: use the export-bytes fallback in `references/workflow.md`.
- If request scope is a single screen: deliver one focused composition plus key states.
- If request scope is product-level: deliver a named pattern board and reusable blocks in addition to the primary screen.

## Guardrails

- Prefer agent-generated designs over `.fig` import as the primary workflow.
- Keep storage flat in `designs/*.fig` unless the repo convention changes.
- Do not assume this repository defines the OpenPencil MCP endpoint; use the upstream OpenPencil runtime and the active editor URL for the current session.
- Do not treat automation bridge state as more authoritative than the visible browser canvas.
- Do not mutate OpenPencil store selection state incorrectly; see `references/workflow.md` for the `selectedIds` rule.
- Prefer `window.__OPEN_PENCIL_STORE__` or `.github/skills/openpencil-design/scripts/openpencil-browser-automation.js` over probing Vue component internals directly.
- Do not assume the visible page still represents the saved design after a scripted export; verify named top-level frames or re-open the saved file.
- Do not stop at a concept write-up when the user expects an actual design artifact.

## Failure Signals

Pause and switch to troubleshooting when any of these appear:

- The editor page name falls back to `Untitled` or the expected frames disappear after a save/export.
- The canvas looks blank even though a `.fig` artifact was produced.
- Runtime notifications appear, especially errors such as `Cannot read properties of undefined (reading 'x')`.
- A scripted session cannot resolve the OpenPencil store or graph from the first Vue component level.
- Browser-native save succeeds inconsistently or file dialogs fail.
- In-page module imports fail for bare specifiers such as `@open-pencil/core`.

## Quality Gates

- Output includes a real Figma-compatible `.fig` when the request expects concrete design work.
- Naming is semantic and reusable, not anonymous shape groups.
- Relevant states are represented: loading, empty, error, and success.
- Handoff is implementation-oriented: sections, components, states, and open risks.
- Saved-artifact validation includes more than file existence: confirm the `.fig` archive is readable and the design can be correlated to expected frame names.
- Repo metadata stays in sync when repository artifacts are updated.

## Reference Files

Read these only when needed:

- `.github/skills/frontend-design/SKILL.md`: visual direction, typography, composition, and anti-generic quality bar.
- `references/workflow.md`: execution order, save/export fallback, runtime gotchas, validation.
- `.github/skills/openpencil-design/scripts/openpencil-browser-automation.js`: stable browser-side helpers for store access, selection repair, and scene summaries.
- `references/prompts.md`: prompt templates for UI screens, reusable pattern boards, and handoff.
- `references/handoff.md`: what to capture when a design becomes implementation-ready.

## Success Criteria

The skill execution is successful when:

- The design exists as a real Figma-compatible `.fig` in the repo `designs/` folder.
- The live design state matches the saved result closely enough for reuse.
- Reusable UI patterns are named and organized when relevant.
- Repo metadata is kept in sync for committed repository changes.
