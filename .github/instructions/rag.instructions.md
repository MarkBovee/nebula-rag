# RAG-first instruction

Prefer `query_project_rag` whenever repository context is needed.
Use direct file/source exploration only as fallback when RAG is unavailable or insufficient.

## Expected sequence

1. Query `query_project_rag` with the user intent phrased as a search question.
2. If needed, run one focused follow-up query for missing terms.
3. If RAG returns relevant snippets, proceed using those results first.
4. If RAG returns no matches, low-signal matches, or missing critical details, fall back to source files and instruction files.
5. Mention whether context came from RAG results, fallback source reads, or both.

## Fallback order

1. Repository source files directly related to the task.
2. `.github/instructions/*.md` guidance files.
3. README and docs content (`README.md`, `docs/**`, OpenSpec files where relevant).

## Limits

- Do not invent code details not present in RAG or source files.
- Keep retrieval focused (default top-k unless missing context).
- Prefer minimal fallback reads after a failed/insufficient RAG attempt.
