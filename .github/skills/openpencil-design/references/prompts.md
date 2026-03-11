# OpenPencil Prompt Templates

Use these as starting points, not rigid scripts.

## General UI Screen Prompt

```text
Design a product UI screen in OpenPencil.

Goals:
- High signal, low noise
- Desktop-first with responsive behavior
- Reusable UI patterns

Include:
- Clear primary task area
- Supporting context or metadata areas
- Action affordances where useful
- Explicit loading, empty, error, and success states

Style:
- Crisp product feel
- Strong hierarchy
- Minimal decoration
- Implementation-friendly structure
```

## Pattern Library Prompt

```text
Extend the current OpenPencil design into a reusable UI pattern board.

Add:
- Primary content card
- Secondary supporting panel
- Status or list card
- Form or input block when relevant
- State examples
- Action chips or CTA rail

Keep naming explicit so the blocks can be reused in later screens.
```

## Implementation Handoff Prompt

```text
Refine this OpenPencil design so it is ready for a Blazor implementation handoff.

Focus on:
- stable section structure
- reusable UI patterns
- repeatable spacing and typography rhythm
- states and edge cases
- labels and naming that map cleanly to components
```

## Save-First Prompt

```text
Save the current live design first as a real .fig asset, then continue refining the reusable pattern section and the main composition.
```