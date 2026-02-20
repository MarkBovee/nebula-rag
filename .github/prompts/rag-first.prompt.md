---
mode: agent
description: "Answer with NebulaRAG context first"
---

Use the MCP tool `query_project_rag` with the exact user request, then answer from the returned snippets.
If the snippets are insufficient, run one additional focused query and merge both results.
