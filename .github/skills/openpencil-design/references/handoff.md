# OpenPencil Handoff Reference

Use this when the user wants the design translated into implementation work.

## Capture These Items

1. Screen or flow purpose
2. Main sections and their order
3. Reusable components or pattern blocks
4. Required UI states
5. Interactions or actions implied by the layout
6. Any data density or responsiveness constraints

## Good Handoff Shape

- One paragraph describing the screen intent.
- One flat list of reusable components.
- One flat list of states.
- One short note on implementation risk or ambiguity.

## Component Naming Guidance

Prefer names that can map directly to UI components, such as:

- `OverviewHero`
- `KpiTile`
- `TrendCard`
- `ActivityFeedCard`
- `SignalDock`
- `StateRail`

## Avoid

- purely visual names with no semantic meaning,
- unnamed rectangles or text groups,
- handoff notes that depend on remembering the canvas visually.