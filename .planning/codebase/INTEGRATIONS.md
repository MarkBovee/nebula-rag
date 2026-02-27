# External Integrations

**Analysis Date:** 2026-02-27

## APIs & External Services

**Vector Search Database:**
- PostgreSQL 16 with pgvector extension
  - Connection: `NEBULARAG_Database__*` environment variables
  - Client: `Npgsql` and `Pgvector` packages
  - Purpose: Stores document chunks and vector embeddings for semantic search

**Developer Tools:**
- GitHub Actions CI/CD
  - Purpose: Automated testing, reindexing, and security scanning
  - Workflows: `rag-reindex.yml`, `security.yml`, `ha-addon-validate.yml`

## Data Storage

**Databases:**
- PostgreSQL (primary)
  - Tables: Indexed documents, chunks, embeddings, projects
  - Connection string built from configuration
  - Vector similarity search capabilities

**File Storage:**
- Local filesystem - Documents are read from local paths during indexing
- No external file storage services detected

**Caching:**
- Not detected - Uses direct database queries

## Authentication & Identity

**Auth Provider:**
- Custom implementation - No external identity providers
- API endpoints are unprotected
- Home Assistant integration may use custom authentication

## Monitoring & Observability

**Error Tracking:**
- Not detected - Basic logging with Serilog

**Logs:**
- Serilog with console output
- OpenTelemetry metrics collection
- Structured logging with JSON formatting

## CI/CD & Deployment

**Hosting:**
- Docker containers via `compose.yaml`
- Multi-container setup: Database + MCP service

**CI Pipeline:**
- GitHub Actions
  - Scheduled RAG reindexing (every 6 hours)
  - On-demand reindexing via workflow dispatch
  - Security scanning
  - Home Assistant addon validation

## Environment Configuration

**Required env vars:**
- `NEBULARAG_Database__Host` - Database host
- `NEBULARAG_Database__Port` - Database port
- `NEBULARAG_Database__Database` - Database name
- `NEBULARAG_Database__Username` - Database username
- `NEBULARAG_Database__Password` - Database password
- `NEBULARAG_Database__SslMode` - SSL mode preference
- `NEBULARAG_PathBase` - Base path for API endpoints

**Secrets location:**
- Configuration files (`ragsettings.local.json`)
- GitHub Secrets for CI/CD
- Environment variables in Docker

## Webhooks & Callbacks

**Incoming:**
- HTTP API endpoints at `/api/*` and `/mcp/*`
- Home Assistant webhook integration potential
- No external webhook endpoints detected

**Outgoing:**
- None detected
- No external API calls from the application

## Home Assistant Integration

**Native Integration:**
- Designed as a Home Assistant add-on
- Dashboard proxy configuration for Home Assistant ingress paths
- Add-on host in `src/NebulaRAG.AddonHost`
- API endpoints designed to work with Home Assistant HTTP requests

**MCP Server:**
- Model Context Protocol server implementation
- Provides semantic search capabilities to AI systems
- Exposes tools: `query_project_rag`, `rag_search_similar`, `rag_get_chunk`
- Memory management tools for AI context

---

*Integration audit: 2026-02-27*
```