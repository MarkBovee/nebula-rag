# NebulaRAG Agent Guide

This file defines the baseline behavior for all coding agents working in this repository.

## Mission

Build and maintain NebulaRAG as a production-ready RAG platform with:

- Clean .NET architecture (`Core`, `Cli`, `Mcp`, `AddonHost`)
- Home Assistant add-on support (ingress UI + MCP endpoint)
- Secure defaults for a public repository
- Readable, maintainable, well-documented code

## Instruction Precedence

Always load and follow these instruction files before implementation:

- `.github/instructions/coding.instructions.md`
- `.github/instructions/documentation.instructions.md`
- `.github/instructions/rag.instructions.md`
- `.github/copilot-instructions.md`
- `editorconfig`

If instruction files conflict, prioritize repository-local instruction files and preserve existing project conventions.

## Coding Standards

- Follow DRY and SOLID.
- Prefer small focused methods.
- Use descriptive names over short generic names.
- Avoid `dynamic`.
- Keep parameter lists concise; introduce request models when argument lists grow.
- Add XML documentation comments for classes and methods.
- Add comments only where intent is not obvious from code.

## Formatting and Style

- Respect `editorconfig` settings.
- Keep C# style and naming consistent with current project files.
- Use block braces for control flow.
- Do not introduce formatting churn outside touched files.

## Quality Gate

Before completing implementation:

1. Build solution with zero errors.
2. Run tests and keep them passing.
3. Ensure docs/readmes are updated for behavior changes.
4. Keep changes scoped; avoid unrelated refactors.

## Documentation Rules

- Put project overview and quick-start updates in `README.md`.
- Put deeper technical guides in `docs/`.
- Do not create temporary progress or report markdown files.

## Add-on and MCP Guidance

- Home Assistant add-on should remain long-running when serving UI and MCP.
- Keep MCP transport contracts stable (`initialize`, `ping`, `tools/list`, `tools/call`).
- Ensure add-on config schema and runtime behavior stay in sync.

## Security Baseline

- Never commit secrets.
- Keep `.nebula.env` out of git.
- Preserve and extend security controls in `.github/` workflows and policies.
