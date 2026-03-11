# Copilot + NebulaRAG default behavior

When answering project-specific questions in this repository:

1. Start with one focused `rag_query` call (`mode: "project"`) for the task/question.
2. Reuse retrieved context; avoid repeated queries unless context is missing, stale, or the task scope changes.
3. If RAG context is empty/low-signal, fall back to direct code search.
4. Cite file paths from RAG snippets (or source reads when fallback is used).

For implementation tasks:

- Query RAG once before proposing code changes.
- Prefer the consolidated Nebula MCP toolset: `rag_query`, `rag_ingest`, `rag_sources`, `rag_admin`, `memory`, `plan`, and `system`.
- Re-query only when validating behavior that depends on newly changed files or unresolved uncertainty.
- Keep changes minimal and scoped to the request.
- Before merge/push to `main`, run dependency freshness checks against latest stable package versions for all detected ecosystems in this repo.
- Always bump `nebula-rag/config.json` add-on version after implemented changes (default patch bump).
- Always add a matching entry to `nebula-rag/CHANGELOG.md` in the same update.
- Do not merge/push `main` when dependency checks fail, or when version/changelog gates are missing.

Memory guidance:

- For project-specific decision history, recurring bug patterns, and conventions, prefer Nebula memory tool actions first (`memory` with `action: "recall"` / `"store"` / `"list"` / `"update"` / `"delete"`).
- For user-level preferences and assistant behavior, prefer VS Code memory.
- When a fact matters to both scopes, dual-write: concise note in VS Code memory plus structured project note in Nebula memory.
- For non-trivial implementation/debug tasks, do one memory recall at start and store at least one Nebula memory before finishing.
- For multi-step tasks, store multiple concise Nebula memories that capture decisions, fixes, and operational conventions.
- Never store secret values in memory; store only secret-source references (for example `.env`).

Planning guidance:

- For multi-step implementation tasks, create a Nebula plan before writing code.
- Use `plan` tool actions in this order: `create` -> `get`/`list` -> `update`/`complete_task`.
- Keep `projectId` explicit (for example `dot-claw`, `NebulaRAG`) and use stable `sessionId` values per workstream.
- Keep plan tasks concrete and execution-oriented (one outcome per task).
- Before ending a non-trivial session, update plan status/task completion to reflect real progress.

Agent setup baseline:

- Keep `AGENTS.md`, `.github/copilot-instructions.md`, `.github/instructions/rag.instructions.md`, and `.github/skills/nebularag/SKILL.md` in target projects.
- Prompt files are optional convenience helpers and are not required for RAG-first behavior.
