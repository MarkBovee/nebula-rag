# RAG-first instruction

Use `query_project_rag` before answering or coding when the task depends on repository context.

## Expected sequence

1. Query with the user request phrased as a search question.
2. If needed, run one follow-up query for missing terms.
3. Proceed with answer/edits using those snippets.
4. Mention when context came from RAG results.

## Limits

- Do not invent code details not present in RAG or source files.
- Keep retrieval focused (default top-k unless missing context).
