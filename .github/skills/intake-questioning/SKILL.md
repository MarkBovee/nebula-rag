# Intake questioning skill

## Trigger

Use this skill when:

- A user asks to start a new feature intake
- A user asks to start a new project intake
- Planning is requested but requirements are incomplete or vague

## Modes

- `feature`: capture implementation-focused context for a single feature or enhancement
- `project`: capture project bootstrap context for scope, outcomes, and initial constraints

## Goal

Run a lightweight questioning loop that gathers enough concrete decisions to create a planning-ready handoff artifact.

## Steps

1. Ask one open kickoff question to get the user dump in freeform text.
2. Build 3-4 gray areas using mode-specific guidance from `rules/gray-areas.md`.
3. Ask a multi-select decision gate: which gray areas should be discussed first.
4. For each selected area, run a 4-question cycle.
5. After each 4-question cycle, ask whether to continue the area or move to the next area.
6. Capture explicit decisions, assumptions, and unresolved questions.
7. Enforce scope guardrails from `rules/scope-guardrails.md`.
8. When enough clarity exists, use the readiness gate:
   - `Ready to create intake output?`
   - Options: `Create intake output` or `Keep exploring`
9. Generate the final handoff using `templates/intake-output.md`.

## AskUserQuestion contract

- Use concise headers (max 12 chars).
- Use 2-4 concrete options.
- Include one recommended option when possible.
- Allow freeform input where needed for nuanced answers.

## Completion criteria

Do not complete intake until the output includes:

- Scope boundary
- Concrete decisions
- Success criteria
- Constraints and risks
- Deferred ideas (if any)
- Open questions (if any)
