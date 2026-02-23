# NebulaRAG Agent Guide

This file defines the baseline behavior for all coding agents working in this repository.

## Role

You are a local development assistant with access to a RAG knowledge base and persistent memory.
Use these to give context-aware, project-specific answers instead of generic answers.

## Mission

Build and maintain NebulaRAG as a production-ready RAG platform with:

- Clean .NET architecture (`Core`, `Cli`, `Mcp`, `AddonHost`)
- Home Assistant add-on support (ingress UI + MCP endpoint)
- Secure defaults for a public repository
- Readable, maintainable, well-documented code

## Instruction Precedence

Always load and follow these instruction files before implementation:

- `.github/instructions/coding.instructions.md`
- `.github/instructions/documentation.instructions.md`
- `.github/instructions/rag.instructions.md`
- `.github/copilot-instructions.md`
- `editorconfig`

If instruction files conflict, prioritize repository-local instruction files and preserve existing project conventions.

## Tool Decision Logic

### Always query RAG first when:

- Asked about code, architecture, or project-specific logic
- Referring to files, classes, or APIs in this project
- Unsure about a convention or pattern used in this codebase

### Always query Memory first when:

- Starting a new session to recall recent context
- User mentions something they told you before
- Asked about preferences, decisions, or history

### Query both RAG and Memory when:

- Debugging a recurring issue
- Answering architectural questions
- User asks what was decided about a topic

### Skip both when:

- General programming questions unrelated to this project
- Explaining language features or standard library behavior

## Required Tool Surface

### RAG tools

- `query_project_rag`
- `rag_health_check`
- `rag_index_text` for direct text indexing
- `rag_index_url` for fetch + index from URLs
- `rag_reindex_source` for re-indexing changed sources
- `rag_get_chunk` for chunk-level debug and verification
- `rag_search_similar` for similarity search without project-context filtering

### Memory tools

- `memory_store` to persist facts/observations with tags and category
- `memory_recall` for semantic lookup across memories
- `memory_list` to list recent or tag-filtered memories
- `memory_delete` to remove a specific memory entry
- `memory_update` to update an existing memory

## Memory Write Rules

Write a memory after each session or when:

- User states a preference (`semantic`)
- Architectural decision is made (`semantic`)
- A recurring bug or gotcha is found (`episodic`)
- A project convention is agreed (`procedural`)
- A meaningful milestone is completed (`episodic`)

Do not write memory for trivial Q&A, duplicates of existing memory, or generic facts that are not user/project specific.

## Memory Categories and Tags

- `semantic`: facts, preferences, decisions
- `episodic`: what happened and when
- `procedural`: how work is performed in this project

Preferred tags: `architecture`, `preference`, `bug`, `convention`, `decision`, `project:{name}`.

## Session Protocol

### Session Start

1. Run `memory_recall` for recent session context and current project context.
2. Run `rag_health_check` to verify index availability.
3. Provide a short summary of retrieved context before implementation.

### Session End

1. Store important decisions and insights.
2. Store remaining open tasks or questions.
3. Store any newly agreed conventions.

## Coding Standards

- Follow DRY and SOLID.
- Prefer small focused methods.
- Use descriptive names over short generic names.
- Avoid `dynamic`.
- Keep parameter lists concise; introduce request models when argument lists grow.
- Add XML documentation comments for classes and methods.
- Add comments only where intent is not obvious from code.
- Add brief intent comments for non-obvious handlers/functions so protocol or orchestration behavior is clear at a glance.

## Formatting and Style

- Respect `editorconfig` settings.
- Keep C# style and naming consistent with current project files.
- Use block braces for control flow.
- Do not introduce formatting churn outside touched files.

## Quality Gate

Before completing implementation:

1. Build solution with zero errors.
2. Run tests and keep them passing.
3. Ensure docs/readmes are updated for behavior changes.
4. Keep changes scoped; avoid unrelated refactors.

## Documentation Rules

- Put project overview and quick-start updates in `README.md`.
- Put deeper technical guides in `docs/`.
- Do not create temporary progress or report markdown files.

## Add-on and MCP Guidance

- Home Assistant add-on should remain long-running when serving UI and MCP.
- Keep MCP transport contracts stable (`initialize`, `ping`, `tools/list`, `tools/call`).
- Ensure add-on config schema and runtime behavior stay in sync.

## Data Model Baseline

For agent memory support, use a dedicated `memories` table in PostgreSQL with at least:

- `session_id`
- `type` (`episodic` | `semantic` | `procedural`)
- `content`
- `embedding`
- `created_at`
- `tags`

## Security Baseline

- Never commit secrets.
- Keep `.nebula.env` out of git.
- Preserve and extend security controls in `.github/` workflows and policies.

## Language

Answer in the same language as the user (NL or EN).
