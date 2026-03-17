<div align="center">

![Nebula RAG Banner](./nebula-banner.svg)

**Production-oriented RAG for code and project knowledge — local, agentic, self-hosted**

[![.NET](https://img.shields.io/badge/.NET-8+-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com) [![Version](https://img.shields.io/badge/version-1.1.0-06b6d4?style=flat-square)](https://github.com/MarkBovee/NebulaRAG/releases)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-pgvector-4169E1?style=flat-square&logo=postgresql)](https://github.com/pgvector/pgvector)
[![MCP](https://img.shields.io/badge/MCP-compatible-06b6d4?style=flat-square)](https://modelcontextprotocol.io)
[![Home Assistant](https://img.shields.io/badge/Home%20Assistant-add--on-41BDF5?style=flat-square&logo=homeassistant)](https://www.home-assistant.io)
[![License: MIT](https://img.shields.io/badge/License-MIT-a855f7?style=flat-square)](LICENSE)

</div>

---

## What is NebulaRAG?

NebulaRAG is a self-hosted Retrieval-Augmented Generation platform that gives AI agents fast, code-aware context from your actual project sources — without sending anything to the cloud.

## MCP Integration for RAG, Memory, and Plans

NebulaRAG includes a broad MCP (Model Context Protocol) integration for retrieval, indexing, memory, and plan lifecycle operations.

### Unified MCP Tools

- `rag_query`: Unified semantic query operations (`project`, `similar`)
- `rag_ingest`: Unified indexing operations (`path`, `text`, `url`, `reindex`)
- `rag_sources`: Unified source operations (`list`, `get_chunk`, `delete`, `normalize`)
- `rag_admin`: Unified admin operations (`init_schema`, `health`, `stats`, `purge`)
- `memory`: Unified memory operations (`store`, `recall`, `list`, `update`, `delete`)
- `plan`: Unified planning operations (`create`, `get`, `list`, `update`, `complete_task`, `update_task`, `archive`)
- `system`: Unified system metadata operations (`server_info`)

### tools/list Profiles

- `tools/list` returns a `minimal` profile with only the unified tool set.

### Plan Session Behavior

Plan retrieval and mutation by `planId` are session-agnostic. `sessionId` is optional for these plan-by-id operations and is used as audit metadata when provided.

### Usage Examples

```bash
# Create a new plan
curl -X POST http://localhost:8099/mcp -d '{
  "method": "tools/call",
  "params": {
    "name": "plan",
    "arguments": {
      "action": "create",
      "sessionId": "agent-session-1",
      "planName": "Project Planning",
      "projectId": "project-123",
      "initialTasks": ["Research requirements", "Design architecture", "Implement features"]
    }
  }
}'

# Get a specific plan
curl -X POST http://localhost:8099/mcp -d '{
  "method": "tools/call",
  "params": {
    "name": "plan",
    "arguments": {
      "action": "get",
      "planId": 123
    }
  }
}'

# List all plans for a session
curl -X POST http://localhost:8099/mcp -d '{
  "method": "tools/call",
  "params": {
    "name": "plan",
    "arguments": {
      "action": "list",
      "sessionId": "agent-session-1"
    }
  }
}'
```

### Security Features

- Optional session metadata for plan/task audit attribution
- Proper error handling for invalid sessions
- JSON-RPC compliance
- Comprehensive logging and error reporting

It combines:
- A **.NET core retrieval engine** — chunking, embeddings, pgvector storage
- A **local CLI** for indexing and querying
- **MCP endpoints** for agents and editor tooling (VS Code, Claude Code, Cursor)
- A **Home Assistant add-on** with a built-in Blazor admin dashboard for overview, RAG, memory, and plan operations

---

## Why NebulaRAG?

- **Local-first** — PostgreSQL + pgvector, nothing leaves your machine
- **Agent-ready** — MCP tooling surface with RAG + persistent memory
- **Lean retrieval defaults** — semantic queries default to a smaller result fan-out, boost exact path hits, and fall back to PostgreSQL full-text search when semantic recall is weak
- **Clean architecture** — core engine, transport adapters, and host runtime are fully separated
- **Operational from day one** — indexing, source management, health checks, and stats all included
- **One-line install** — PowerShell setup script handles MCP registration for VS Code and Claude Code

---

## Roadmap

The current product roadmap is tracked in `ROADMAP.md`.

That roadmap is focused on making NebulaRAG the default setup, MCP, RAG, memory, planning, and session-continuity stack for Nebula-centric workflows.

---

## What is RAG?

Retrieval-Augmented Generation augments AI models with retrieved context from an indexed corpus. NebulaRAG indexes your code, docs, tests, and architecture notes so agent queries are grounded in your actual sources — not hallucinated guesses.

**Common uses:**
- Code-aware Q&A and onboarding
- Investigating complex or high-risk code areas
- Understanding indexing and storage flows
- Auditing registered sources and index health
- Auto-assisting pull requests with precise context

---

## Quick Install (Windows + PowerShell)

```powershell
$scriptUrl='https://raw.githubusercontent.com/MarkBovee/NebulaRAG/main/scripts/setup-nebula-rag.ps1'
$scriptPath=Join-Path ([System.IO.Path]::GetTempPath()) 'setup-nebula-rag.ps1'
Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue
Invoke-WebRequest -Uri $scriptUrl -OutFile $scriptPath -ErrorAction Stop
& $scriptPath
```

Downloads the setup script and configures user-level MCP registration for VS Code and Claude Code.

The same script is also the canonical way to scaffold Nebula instruction files into a project.

---

## Quick Start

### Local CLI

```powershell
dotnet run --project src\NebulaRAG.Cli -- init
dotnet run --project src\NebulaRAG.Cli -- index --source .
dotnet run --project src\NebulaRAG.Cli -- query --text "How is MCP transport handled?"
```

### MCP (stdio)

```powershell
dotnet run --project src\NebulaRAG.Mcp -- --config src\NebulaRAG.Cli\ragsettings.json
```

### Docker Compose

```powershell
cp .env.example .env
# edit .env with your database settings
docker compose up -d
```

### Home Assistant Add-on

1. Add repository: `https://github.com/MarkBovee/NebulaRAG`
2. Install the **Nebula RAG** add-on
3. Configure `database.*` options
4. Start the add-on and open ingress

The ingress dashboard exposes a project switcher and four operator tabs:

- `Project switcher` to keep the admin surface centered on one project when doing CRUD or investigation work.
- `Overview` for health, telemetry, activity, and project breakdown.
- `Rag` for semantic query, indexing, and source maintenance.
- `Memory` for scoped analytics, recall, and memory editing.
- `Plans` for session-filtered plans, task updates, and status changes.

| Endpoint | URL |
|---|---|
| UI | `http://homeassistant.local:8099/nebula/` |
| Dashboard | `http://homeassistant.local:8099/nebula/dashboard/` |
| MCP JSON-RPC | `http://homeassistant.local:8099/nebula/mcp` |

---

## Setup Script Examples

**User-level setup — Home Assistant add-on:**
```powershell
pwsh -File .\scripts\setup-nebula-rag.ps1 `
  -Mode User `
  -ClientTargets Both `
  -InstallTarget HomeAssistantAddon `
  -HomeAssistantMcpUrl http://homeassistant.local:8099/nebula/mcp `
  -Force
```

**Local container mode:**
```powershell
pwsh -File .\scripts\setup-nebula-rag.ps1 `
  -Mode User `
  -InstallTarget LocalContainer `
  -CreateEnvTemplate `
  -Force
```

---

## System Overview

```
┌─────────────────────────────────────────────────────────────┐
│           AI Agent / Editor                                  │
│   Claude Code · VS Code Copilot · Cursor · CLI              │
└────────────────────────┬────────────────────────────────────┘
                         │  MCP (stdio or HTTP JSON-RPC)
         ┌───────────────┴──────────────────┐
         │         NebulaRAG Server          │
         │  ┌──────────┐  ┌──────────────┐  │
         │  │ MCP Tools│  │ REST / Ingress│  │
         │  └────┬─────┘  └──────┬───────┘  │
         │       └────────┬──────┘          │
         │         ┌──────▼──────┐          │
         │         │ Core Engine │          │
         │         │ RAG + Memory│          │
         │         └──────┬──────┘          │
         └────────────────┼─────────────────┘
                          │
          ┌───────────────▼──────────────────┐
          │     PostgreSQL + pgvector         │
          │  ┌──────────┐  ┌──────────────┐  │
          │  │  chunks  │  │   memories   │  │
          │  └──────────┘  └──────────────┘  │
          └──────────────────────────────────┘
```

---

## Project Structure

```
NebulaRAG/
├── src/
│   ├── NebulaRAG.Core/        # Chunking, embeddings, storage, query services
│   ├── NebulaRAG.Cli/         # CLI: init · index · query
│   ├── NebulaRAG.Mcp/         # stdio MCP adapter
│   └── NebulaRAG.AddonHost/   # HTTP host: Home Assistant ingress + MCP endpoint + Blazor dashboard
├── container/                 # Container configuration
├── nebula-rag/                # Home Assistant add-on package + release metadata
├── scripts/                   # PowerShell setup scripts
├── designs/                   # Design artifacts and working assets
├── tests/
│   └── NebulaRAG.Tests/
├── .mcp.json                  # MCP config (Claude Code)
├── copilot.mcp.json           # MCP config (VS Code Copilot)
├── ROADMAP.md                 # Product roadmap and adoption phases
├── AGENTS.md                  # Agent instruction file
├── compose.yaml               # Docker Compose stack
└── .env.example               # Environment template
```

---

## MCP Tools

### RAG

| Tool | Description |
|---|---|
| `rag_query` | Unified semantic query operations (`project`, `similar`) |
| `rag_ingest` | Unified indexing operations (`path`, `text`, `url`, `reindex`) |
| `rag_sources` | Unified source operations (`list`, `get_chunk`, `delete`, `normalize`) |
| `rag_admin` | Unified admin operations (`init_schema`, `health`, `stats`, `purge`) |
| `system` | Runtime server metadata (`server_info`) |

`rag_ingest` with `mode: "path"` accepts both directory and single-file source paths.

### Memory

| Tool | Description |
|---|---|
| `memory` | Unified memory operations (`store`, `recall`, `list`, `update`, `delete`) |

### Plans

| Tool | Description |
|---|---|
| `plan` | Unified plan/task operations (`create`, `get`, `list`, `update`, `complete_task`, `update_task`, `archive`) |

`plan` with `action: "update_task"` supports task statuses `pending`, `in_progress`, `completed`, and `failed` (validated transitions).

> The `memories` table and indexes are created automatically through `rag_admin` with action `init_schema`.

---

## Agent Setup

NebulaRAG ships with `AGENTS.md` — an instruction file that tells your agent when to query RAG vs memory, when to write to memory, and which conventions this project follows.

NebulaRAG now also ships with `.github/nebula.instructions.md` as the canonical human-facing setup and operating guide. The setup script scaffolds this file alongside the client-specific compatibility files.

The current scaffolded instruction bundle is:

- `.github/nebula.instructions.md`
- `AGENTS.md`
- `.github/copilot-instructions.md`
- `.github/instructions/rag.instructions.md`
- `.github/skills/nebularag/SKILL.md`

MCP configs for Claude Code (`.mcp.json`) and VS Code Copilot (`copilot.mcp.json`) are included in the repository root and registered automatically by the setup script.

### Intake Prompt Commands

For lightweight planning intake, this repository now includes two prompt commands:

- `/new-feature` - run a short questioning loop to capture feature-level decisions and constraints
- `/new-project` - run a short questioning loop to capture project-level scope and outcomes

Both commands use the shared intake skill at `.github/skills/intake-questioning/SKILL.md` and produce a structured handoff that can be used as planning input.

---

## Security

- Vulnerability policy: [`SECURITY.md`](SECURITY.md)
- Code ownership: [`.github/CODEOWNERS`](.github/CODEOWNERS)
- Dependency maintenance: [`.github/dependabot.yml`](.github/dependabot.yml)
- Security workflow: [`.github/workflows/security.yml`](.github/workflows/security.yml)

**Never commit secrets or credential-bearing env files.**

---

## Contributing

1. Create a branch from `main`
2. Keep changes scoped — follow the instructions in `AGENTS.md`
3. Run tests and checks
4. Open a PR with a clear change summary

---

<div align="center">

Built for developers who want AI that knows their codebase — not someone else's.

</div>
