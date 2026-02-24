# Copilot + NebulaRAG default behavior

When answering project-specific questions in this repository:

1. Start with one focused `query_project_rag` call for the task/question.
2. Reuse retrieved context; avoid repeated queries unless context is missing, stale, or the task scope changes.
3. If RAG context is empty/low-signal, fall back to direct code search.
4. Cite file paths from RAG snippets (or source reads when fallback is used).

For implementation tasks:

- Query RAG once before proposing code changes.
- Re-query only when validating behavior that depends on newly changed files or unresolved uncertainty.
- Keep changes minimal and scoped to the request.

Agent setup baseline:

- Keep `AGENTS.md`, `.github/copilot-instructions.md`, `.github/instructions/rag.instructions.md`, and `.github/skills/nebularag/SKILL.md` in target projects.
- Prompt files are optional convenience helpers and are not required for RAG-first behavior.
