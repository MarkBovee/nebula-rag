---
name: nebularag
description: NebulaRAG repository skill for RAG-first retrieval, project memory, and plan-driven execution using the unified MCP tools.
license: Complete terms in LICENSE.txt
---

# NebulaRAG Skill

Use this skill for project-specific implementation, debugging, and architecture questions in this repository.

## Preferred tool surface

- `rag_query` with `mode: "project"` (first lookup)
- `rag_ingest` with `mode: "path"|"text"|"url"|"reindex"`
- `rag_sources` with `action: "list"|"get_chunk"|"delete"|"normalize"`
- `rag_admin` with `action: "health"|"stats"|"init_schema"|"purge"`
- `memory` with `action: "recall"|"list"|"store"|"update"|"delete"`
- `plan` with `action: "create"|"get"|"list"|"update"|"update_task"|"complete_task"|"archive"`
- `system` with `action: "server_info"`

## Execution workflow

1. Recall memory context first for non-trivial tasks (`memory` + `recall` or `list`).
2. Run one focused `rag_query` (`mode: "project"`) with the user goal.
3. Reuse retrieved context; run a follow-up query only when required details are still missing.
4. Fall back to direct source reads only when RAG signal is insufficient.
5. For multi-step implementation work, create/update a plan and complete tasks as work finishes.
6. Before ending non-trivial sessions, store concise project learnings with `memory` (`action: "store"`).

## Guardrails

- Do not store secrets in memory; only store secret source references.
- Keep stored memories concise and deduplicated.
- Keep code changes minimal and scoped to the user request.
- Follow repository instruction files and coding standards first when conflicts occur.
