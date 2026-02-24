# NebulaRAG retrieval skill

## Trigger

Use this skill for:

- Codebase questions
- Refactoring impact analysis
- "Where is X implemented?" requests

## Steps

1. If the task may depend on prior decisions or recurring issues, call `memory_recall` first.
2. When available, call `memory_list` to avoid duplicate writes and to confirm recent context.
3. Call `query_project_rag` with the user intent.
4. Extract relevant file paths and snippets.
5. Continue with implementation/review using those references.
6. Persist durable project insights with `memory_store` (at least 1 for non-trivial tasks; 2-5 for multi-step sessions).

## Tool contract

- Tool name: `query_project_rag`
- Input: `{ "text": "<question>", "limit": 5 }`
- Output: scored snippets with source path and chunk index

## Memory contract

- Recall: `memory_recall` for project decision history and recurring bug patterns.
- Review recent: `memory_list` to avoid duplicates and check recent context.
- Persist: `memory_store` for non-trivial project insights, decisions, and conventions.
- Safety: never store secrets; store only references to secret files/locations.
