# Stack Research

**Domain:** Plan/Task Lifecycle Management for .NET + PostgreSQL RAG Application
**Researched:** 2026-02-27
**Confidence:** MEDIUM

## Recommended Stack

### Core Technologies

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| .NET 10.0 | 10.0 | Runtime framework | Already in use, no migration needed |
| PostgreSQL | 16+ | Plan data storage | Already in use, supports required constraints and indexes |
| Npgsql | 10.0.1 | PostgreSQL client | Already in use, supports advanced PostgreSQL features |
| Dapper | 2.1.35 | ORM for simple CRUD | Lightweight, matches existing NebulaRAG patterns |

### Supporting Libraries

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| (None) | - | Use existing Dapper patterns | Leverage established conventions |

### Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| (None) | - | Use existing development environment |

## Installation

```bash
# Core (already installed)
# Dapper
dotnet add package Dapper --version 2.1.35
```

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| Dapper | Entity Framework Core | If complex relationships/graph queries needed (not MVP) |
| Plain tables + FK constraints | JSONB columns for flexible metadata | If task/plan fields are variable (not MVP) |

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| Entity Framework Core | Overkill for simple parent-child CRUD, adds migration complexity | Dapper with explicit SQL |
| New database instance | Single schema simplifies operations | Share existing PostgreSQL instance |
| Redis for caching | Adds complexity, PostgreSQL suffices for MVP scale | In-memory caching if needed later |
| Distributed coordination systems | Overkill for single-instance "one active per session" | PostgreSQL advisory locks if multi-instance needed |

## Stack Patterns by Variant

**If task metadata is highly variable:**
- Use JSONB column for task metadata
- Because fields may change frequently
- Note: Adds query complexity

**If task metadata is stable:**
- Use explicit columns (title, description, status, priority)
- Because type-safe and queryable
- Note: Schema migrations required for new fields

**If multi-instance deployment needed:**
- Use PostgreSQL advisory locks or row-level SELECT FOR UPDATE
- Because "one active per session" requires coordination
- Note: Adds lock management complexity

## Version Compatibility

| Package A | Compatible With | Notes |
|-----------|-----------------|-------|
| Npgsql 10.0.1 | .NET 10.0 | Already verified in existing codebase |
| Dapper 2.1.35 | Npgsql 10.0.1 | Standard Dapper+PostgreSQL pairing |

## Sources

- Existing NebulaRAG codebase analysis
- PostgreSQL 16 documentation for CHECK constraints and partial indexes
- Dapper documentation for parent-child relationship patterns

---
*Stack research for: Plan/Task Lifecycle Management*
*Researched: 2026-02-27*
