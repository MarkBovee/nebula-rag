# RAG-first instruction

Prefer `rag_query` with `mode: "project"` whenever repository context is needed.
Use direct file/source exploration only as fallback when RAG is unavailable or insufficient.

## Memory-assisted retrieval

Use memory and RAG together with explicit scope:

- Use Nebula memory tools first for project decision history, recurring bugs, and prior architectural choices.
- Use VS Code memory tool first for user preferences and assistant interaction behavior.
- Use `rag_query` (`mode: "project"`) for current codebase facts and implementation details.
- For non-trivial implementation/debug tasks, store at least one Nebula memory before finishing.
- For multi-step tasks, store 2-5 concise Nebula memories covering decisions, fixes, and operational conventions.
- Never store secret values in memory; store only references to secret locations (for example `.env`).

If memory and RAG disagree, treat source code/RAG-backed source snippets as the implementation truth and call out the discrepancy.

## Expected sequence

0. For multi-step implementation work, initialize or load a Nebula plan (`plan` action `create` or `list`/`get`) before coding.
1. Query Nebula memory for related prior decisions/issues when task history matters.
2. Query `rag_query` with `mode: "project"` and the user intent phrased as a search question.
3. If needed, run one focused follow-up query for missing terms.
4. If RAG returns relevant snippets, proceed using those results first.
5. If RAG returns no matches, low-signal matches, or missing critical details, fall back to source files and instruction files.
6. Mention whether context came from memory, RAG results, fallback source reads, or a combination.
7. Before finishing non-trivial tasks, persist essential project insights with `memory` using `action: "store"`.
8. Before finishing non-trivial tasks, update plan progress using `plan` actions `update` and `complete_task` where applicable.

## Fallback order

1. Repository source files directly related to the task.
2. `.github/instructions/*.md` guidance files.
3. README and docs content (`README.md`, `docs/**`, OpenSpec files where relevant).

## Limits

- Do not invent code details not present in RAG or source files.
- Keep retrieval focused (default top-k unless missing context).
- Prefer minimal fallback reads after a failed/insufficient RAG attempt.
