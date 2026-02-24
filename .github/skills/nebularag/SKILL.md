# NebulaRAG retrieval skill

## Trigger

Use this skill for:

- Codebase questions
- Refactoring impact analysis
- "Where is X implemented?" requests

## Steps

1. If the task may depend on prior decisions or recurring issues, call `memory_recall` first.
2. Call `query_project_rag` with the user intent.
3. Extract relevant file paths and snippets.
4. Continue with implementation/review using those references.
5. When a durable project insight is produced, persist it with `memory_store`.

## Tool contract

- Tool name: `query_project_rag`
- Input: `{ "text": "<question>", "limit": 5 }`
- Output: scored snippets with source path and chunk index

## Memory contract

- Recall: `memory_recall` for project decision history and recurring bug patterns.
- Persist: `memory_store` for non-trivial project insights, decisions, and conventions.
