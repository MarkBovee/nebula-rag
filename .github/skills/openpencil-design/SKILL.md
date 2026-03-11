---
name: openpencil-design
description: OpenPencil-first UI and product design skill. Use this when the user asks to create or refine screens, build reusable patterns, save or export .fig files, or translate live canvas work into implementation-ready handoff, even when OpenPencil is not named explicitly.
license: Complete terms in LICENSE.txt
---

# OpenPencil Design Skill

Use this skill when the task is to create or evolve UI designs in OpenPencil or a similar browser-first canvas tool.

This skill captures a browser-first workflow, reliable save/export behavior, and handoff expectations for reusable UI work.

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
- If OpenPencil helper scripts exist, they are typically under `scripts/openpencil/`.
- The local editor is typically browser-first, not desktop-app-first.
- If a visible OpenPencil browser page is open, treat that page as the source of truth for design state.
- If a sibling local upstream checkout exists at `../open-pencil`, prefer that real repo/editor over hosted shortcuts.

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
3. If a browser page is open, inspect the live page first.
4. Prefer editing the visible page or local upstream OpenPencil repo over abstract-only design descriptions.
5. Save work as a real Figma-compatible `.fig`, not only as prose or screenshots.
6. If save dialogs are unreliable, use the in-page export fallback described in `references/workflow.md`.
7. For substantial work, preserve reusable output as a named pattern board or baseline.
8. Validate touched files and keep related metadata aligned with the host repository policy.

If the visible page resets, goes blank, or drifts from the saved artifact, treat the saved `.fig` plus explicit validation as the source of truth until the file is re-opened successfully.

## Decision Points

- If a live canvas exists: continue from the visible page first.
- If no live canvas exists: start or reuse the local OpenPencil flow via `scripts/openpencil/`.
- If browser save works: use native save.
- If browser save fails: use the export-bytes fallback in `references/workflow.md`.
- If request scope is a single screen: deliver one focused composition plus key states.
- If request scope is product-level: deliver a named pattern board and reusable blocks in addition to the primary screen.

## Guardrails

- Prefer agent-generated designs over `.fig` import as the primary workflow.
- Keep storage flat in `designs/*.fig` unless the repo convention changes.
- Do not rely on `http://localhost:3100/` as a UI route; `/mcp` is the expected endpoint.
- Do not treat automation bridge state as more authoritative than the visible browser canvas.
- Do not mutate OpenPencil store selection state incorrectly; see `references/workflow.md` for the `selectedIds` rule.
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

- `references/workflow.md`: execution order, save/export fallback, runtime gotchas, validation.
- `references/prompts.md`: prompt templates for UI screens, reusable pattern boards, and handoff.
- `references/handoff.md`: what to capture when a design becomes implementation-ready.

## Success Criteria

The skill execution is successful when:

- The design exists as a real Figma-compatible `.fig` in the repo `designs/` folder.
- The live design state matches the saved result closely enough for reuse.
- Reusable UI patterns are named and organized when relevant.
- Repo metadata is kept in sync for committed repository changes.
