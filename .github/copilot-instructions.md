# Copilot + NebulaRAG default behavior

When answering project-specific questions in this repository:

1. First call the MCP tool `query_project_rag` with the user question.
2. Use returned chunks as primary repository context.
3. If RAG context is empty, fall back to direct code search.
4. Cite file paths from RAG snippets in the response.

For implementation tasks:

- Query RAG before proposing code changes.
- Re-query after edits when validating impacted behavior.
- Keep changes minimal and scoped to the request.
