---
mode: ask
tools:
  - read_file
  - vscode_askQuestions
  - create_file
  - apply_patch
description: Start lightweight intake for a new feature using a shared questioning loop.
---

Use `.github/skills/intake-questioning/SKILL.md` in `feature` mode.

Execution requirements:

1. Ask one open freeform kickoff question about the feature goal.
2. Build 3-4 feature-specific gray areas using `.github/skills/intake-questioning/rules/gray-areas.md`.
3. Ask a multi-select gate for which areas to discuss.
4. For each selected area, run 4 targeted questions, then ask whether to continue the area or move on.
5. Apply `.github/skills/intake-questioning/rules/scope-guardrails.md` during the loop.
6. End with readiness gate:
   - `Create intake output`
   - `Keep exploring`
7. Produce final structured output using `.github/skills/intake-questioning/templates/intake-output.md`.

Output intent:

- A planning-ready feature intake artifact with explicit decisions, constraints, success criteria, and deferred ideas.
