# NebulaRAG Architecture & Vision

This document describes the current architecture, the target production model, and the operational guardrails used as the project matures.

## Public Repository Baseline (Current)

The project is now public and includes repository-level hardening controls:

- `SECURITY.md` for private vulnerability reporting guidance.
- `.github/CODEOWNERS` for maintainer ownership on sensitive paths.
- `.github/dependabot.yml` for automated dependency update PRs.
- `.github/workflows/security.yml` for CodeQL and NuGet vulnerability audit.
- `.github/workflows/ha-addon-validate.yml` for add-on package validation.

These controls are part of the deployment architecture because they gate code flow and reduce supply-chain risk.

## Current Architecture (Development)

```
┌─────────────────────────────────────────────────────────────┐
│                    Copilot / Local Projects                 │
├─────────────────────────────────────────────────────────────┤
         │                           │
         ▼ (MCP)                     ▼ (CLI)
    ┌────────────┐           ┌─────────────────┐
    │ MCP Server │◄──────────┤  CLI Tool       │
    │(stdio JSON)│           │ (init/index     │
    └────────────┘           │  query/admin)   │
         │                   └─────────────────┘
         │
    ┌────────────────────────────────────────┐
    │    NebulaRAG.Core Services             │
    │  ├─ RagIndexer                         │
    │  ├─ RagQueryService                    │
    │  ├─ TextChunker                        │
    │  └─ HashEmbeddingGenerator             │
    └────────────────────────────────────────┘
         │
    ┌────────────────────────────────────────┐
    │    PostgresRagStore                    │
    │    ├─ UpsertDocument                   │
    │    ├─ Search                           │
    │    └─ InitializeSchema                 │
    └────────────────────────────────────────┘
         │
    ┌────────────────────────────────────────┐
    │      PostgreSQL + pgvector             │
    │  ├─ rag_documents                      │
    │  ├─ rag_chunks                         │
    │  └─ IVFFlat vector index               │
    └────────────────────────────────────────┘
```

**Limitations**:
- No management interface
- No observability (logging, metrics)
- Credentials hardcoded in configs
- No data organization (topics)
- Limited error handling

---

## Target Production Architecture (After Phase 4)

```
┌────────────────────────────────────────────────────────────────────┐
│                   External Integrations                             │
├───────────┬──────────────┬──────────────┬──────────────────────────┤
│ Copilot   │ Local Agent  │ Third-party  │ Observability Stack      │
│ (MCP)     │ Projects     │ Tools        │ (Logs, Metrics, Alerts)  │
└───────────┴──────────────┴──────────────┴──────────────────────────┘
     │          │                               │
     ▼          ▼                               │
  ┌──────────────────┐                         │ (Structured Logs/Metrics)
  │   MCP Server     │────┐                    │
  │  (Container)     │    │    ┌────────────────▼──────────┐
  └──────────────────┘    │    │  Observability Collector   │
                          │    │  (Prometheus, ELK, etc)    │
  ┌──────────────────┐    │    └─────────────────┬──────────┘
  │   CLI Tool       │────┼──────┐               │
  │  (Local)         │    │      │               │
  └──────────────────┘    │      │               ▼
                          │      ▼          ┌──────────────┐
                          │   ┌──────────────┤  Dashboard   │
                          │   │  API Layer   │  Monitoring  │
                          │   │ (ASP.NET)    │  Alerting    │
                          └──►│              └──────────────┘
                              │  ├─ /api/stats
       ┌──────────────────────┤  ├─ /api/documents
       │                      │  ├─ /api/topics
       │                      │  ├─ /api/admin
       │                      └──┤─ Secured auth
       │                         │
       │   ┌─────────────────────┤
       │   │   ┌────────────────────────────────┐
       │   └──►│    Admin Web UI (React)         │
       │       │  ├─ Stats Dashboard            │
       │       │  ├─ Documents Manager          │
       │       │  ├─ Topics Organizer           │
       │       │  └─ Indexing Logs              │
       │       └────────────────────────────────┘
       │
       ▼
     ┌────────────────────────────────────────┐
     │    NebulaRAG.Core Services             │
     │  ├─ RagIndexer                         │
     │  ├─ RagQueryService                    │
     │  ├─ RagManagementService◄─── NEW       │
     │  ├─ TextChunker                        │
     │  ├─ HashEmbeddingGenerator             │
     │  └─ / Pluggable LLM embedding gen.    │
     └────────────────────────────────────────┘
            │            │
            ▼            ▼ (logging)
     ┌────────────────────────────────────────┐
     │    PostgresRagStore                    │
     │    ├─ CRUD operations                  │
     │    ├─ Search with scoring              │
     │    ├─ Topic filtering                  │
     │    ├─ Management ops (delete, stats)   │
     │    └─ Schema versioning                │
     └────────────────────────────────────────┘
            │
         ┌──┴───────────────────────────┐
         │  PostgreSQL 14+ + pgvector   │
         │  ├─ rag_topics◄────── NEW    │
         │  ├─ rag_documents            │
         │  ├─ rag_chunks               │
         │  │  ├─ IVFFlat index         │
         │  │  ├─ Full-text index       │
         │  │  └─ Topic FK               │
         │  └─ Proper constraints/audit │
         │
         │  ┌─ Backups (pg_dump)        │
         │  ├─ Replication (optional)   │
         │  └─ Performance monitoring   │
         └──────────────────────────────┘
```

**Enhancements**:
- ✅ REST API for programmatic access
- ✅ React web UI for visual management
- ✅ Structured logging & observability
- ✅ Topic/category organization
- ✅ Management service with full CRUD
- ✅ Secrets management (no hardcoded credentials)
- ✅ Comprehensive error handling
- ✅ Integration tests & CI/CD
- ✅ Production-grade reliability

---

## Component Responsibilities

### 1. MCP Server (NebulaRAG.Mcp)
**Responsibility**: Expose RAG capabilities to Copilot via MCP protocol

**Tools**:
- `query_project_rag` - Query with optional filters
- `rag_health_check` - Check system status
- `rag_server_info` - Server metadata
- `rag_index_stats` - Statistics
- `rag_recent_sources` - Recent documents
- `rag_get_stats` - Detailed stats (NEW)
- `rag_list_sources` - All sources (NEW)
- `rag_delete_source` - Delete by source (NEW)
- `rag_purge_all` - Clear all data (NEW)

**Runs**: Container via stdio or locally

---

### 2. CLI Tool (NebulaRAG.Cli)
**Responsibility**: Local command-line interface for development/admin

**Commands**:
- `init` - Initialize database schema
- `index --source <path>` - Index directory
- `query --text <query>` - Search
- `stats` - Show statistics (NEW)
- `list-sources` - Show indexed sources (NEW)
- `delete --source <path>` - Delete source (NEW)
- `purge-all` - Clear all (NEW)
- `health-check` - Verify connectivity (NEW)

---

### 3. REST API (NebulaRAG.Api) - Phase 2
**Responsibility**: HTTP interface for web UI and external integrations

**Endpoints**:
- `GET /api/health` - Health status
- `GET /api/stats` - Index statistics
- `GET /api/documents` - List documents (paginated)
- `GET /api/documents/{id}` - Get document details
- `GET /api/documents/{id}/chunks` - Get chunks for document
- `DELETE /api/documents/{id}` - Delete document
- `DELETE /api/purge` - Admin purge with confirmation
- `GET /api/topics` - List topics
- `POST /api/topics` - Create topic
- `PUT /api/documents/{id}/topic` - Assign topic

**Security**: API key authentication on management endpoints

---

### 4. Admin UI (admin-ui) - Phase 2
**Responsibility**: Visual management and monitoring dashboard

**Views**:
- Dashboard: Overall statistics, recent activity
- Documents: Browse, search, delete, tag with topics
- Topics: Create, edit, assign to documents
- Logs: Recent indexing operations and errors
- Settings: Configure API connection (read-only for now)

**Tech**: React 18 + Vite + TypeScript + Tailwind CSS

---

### 5. Core Services (NebulaRAG.Core)
**Responsibility**: Business logic and data operations

**Services**:
- `RagIndexer` - Document chunking and embedding
- `RagQueryService` - Semantic search
- `RagManagementService` - Admin operations (NEW)

**Supporting**:
- `TextChunker` - Text segmentation
- `IEmbeddingGenerator` - Vector generation (pluggable)
- `PostgresRagStore` - Data access layer

---

### 6. PostgreSQL Database
**Responsibility**: Persistent storage with vector support

**Schema**:
```sql
rag_topics (id, name, description, created_at)
rag_documents (id, source_path, content_hash, indexed_at, topics[])
rag_chunks (id, document_id, chunk_index, chunk_text, token_count, embedding, topic_id)
```

**Indexes**:
- IVFFlat on embeddings (vector search)
- GIST/GIN on text (full-text search)
- Compound keys for integrity

---

## Data Flow Examples

### Example 1: User Indexes Code
```
User Terminal
    │
    ├─ dotnet run --project src/NebulaRAG.Cli -- index --source ./src
    │
    ▼
CLI Program.cs
    │
    ├─ Load config from ragsettings.json
    │
    ├─ Create RagIndexer
    │
    ▼
RagIndexer.IndexDirectoryAsync()
    │
    ├─ Enumerate files (*.cs, *.md, etc)
    │
    ├─ For each file:
    │   ├─ Read content
    │   ├─ TextChunker.Chunk() ─► chunks
    │   ├─ HashEmbeddingGenerator.GenerateEmbedding() ─► vectors
    │   └─ PostgresRagStore.UpsertDocument() ─► DB
    │
    ▼
PostgreSQL
    │
    ├─ INSERT/UPDATE rag_documents
    ├─ INSERT rag_chunks with embeddings
    ├─ Trigger IVFFlat index update
    │
    ▼
CLI Console Output
    └─ "Index complete: 42 documents, 1,205 chunks indexed"
```

### Example 2: Copilot Queries
```
Copilot (in VS Code)
    │
    ├─ "How does the chunker work?"
    │
    ▼
MCP Server (Query tool)
    │
    ├─ Receive query text
    ├─ Generate embedding via HashEmbeddingGenerator
    │
    ▼
RagQueryService.QueryAsync()
    │
    ├─ Call PostgresRagStore.SearchAsync()
    │
    ▼
PostgreSQL
    │
    ├─ SELECT * FROM rag_chunks
    │  ORDER BY embedding <-> @query_vector
    │  LIMIT 5
    │
    ▼
Results (scored chunks)
    │
    ├─ Back to MCP Server
    ├─ Format as tool result
    │
    ▼
Copilot Context
    └─ Passes to LLM as context
```

### Example 3: Admin Views Stats
```
Browser
    │
    ├─ http://localhost:3000/admin
    │
    ▼
Admin UI (React)
    │
    ├─ GET /api/stats
    │
    ▼
REST API (ASP.NET)
    │
    ├─ RagManagementService.GetStatsAsync()
    │
    ▼
PostgresRagStore.GetIndexStatsAsync()
    │
    ├─ SQL query: COUNT documents, chunks, tokens
    │
    ▼
PostgreSQL (single query)
    │
    ├─ Returns: {docCount: 42, chunkCount: 1205, tokens: 45000}
    │
    ▼
REST API
    │
    ├─ JSON response
    │
    ▼
AdminUI
    │
    ├─ Render dashboard cards
    ├─ Chart of indexing timeline
    │
    ▼
Browser Display
    └─ "42 documents indexed, 1.2K chunks, 45K tokens"
```

---

## Deployment Models

### Model 1: Local Development (Current - Phase 1)
```
Developer Machine
├─ NebulaRAG.Cli
├─ NebulaRAG.Mcp (running via podman/docker)
└─ PostgreSQL (local or remote)
```

### Model 2: Local + Admin (Phase 2)
```
Developer Machine
├─ NebulaRAG.Cli (CLI commands)
├─ NebulaRAG.Mcp (MCP server)
├─ NebulaRAG.Api (ASP.NET server on :5000)
├─ Admin UI (React dev server on :3000)
└─ PostgreSQL (Docker container)
```

### Model 3: Production Ready (Phase 4)
```
Production Environment (Kubernetes or Docker Compose)
├─ NebulaRAG.Api (1+ replicas)
│  ├─ Load balancer (nginx/Envoy)
│  ├─ Metrics exporter (Prometheus)
│  └─ Structured logging → ELK/CloudWatch
├─ NebulaRAG.Mcp (1+ replicas)
│  └─ Via stdio or gRPC sidecar
├─ Admin UI (CDN)
│  └─ Static asset serving + fallback
├─ PostgreSQL (Primary + Replica)
│  ├─ Connection pooling (PgBouncer)
│  ├─ Regular backups
│  └─ Replication for HA
└─ Monitoring
   ├─ Prometheus + Grafana
   ├─ Alert rules (high latency, errors)
   └─ Distributed tracing (optional Jaeger)
```

---

## Key Principles

1. **Separation of Concerns**: Each component has a single responsibility
2. **Testability**: All services are injectable and mockable
3. **Observability**: Structured logging at every boundary
4. **Security**: Credentials never hardcoded, API key validation
5. **Performance**: Optimized queries, proper indexing, connection pooling
6. **Reliability**: Graceful error handling, retry logic, health checks
7. **Scalability**: Stateless API, database as single source of truth
8. **Maintainability**: Clear patterns, documentation, examples

---

## Success Metrics (Post-Phase 4)

| Metric | Target | Measurement |
|--------|--------|-------------|
| API Response Time (p99) | <100ms | Prometheus histogram |
| Indexing Throughput | >1K chunks/sec | Benchmark test |
| Query Accuracy (top-1) | >85% | Manual evaluation |
| System Uptime | >99.9% | Monitoring alerts |
| Code Coverage | >80% | Test reports |
| Documentation | 100% | API + Admin guides |
| Deployment Time | <5min | CI/CD pipeline |

