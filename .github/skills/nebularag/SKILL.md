# NebulaRAG retrieval skill

## Trigger

Use this skill for:

- Codebase questions
- Refactoring impact analysis
- "Where is X implemented?" requests

## Steps

1. Call `query_project_rag` with the user intent.
2. Extract relevant file paths and snippets.
3. Continue with implementation/review using those references.

## Tool contract

- Tool name: `query_project_rag`
- Input: `{ "text": "<question>", "limit": 5 }`
- Output: scored snippets with source path and chunk index
