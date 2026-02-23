# NebulaRAG Production Readiness Plan

## Executive Summary
NebulaRAG is a lightweight PostgreSQL-backed RAG system designed for local AI projects. Currently in development phase with core functionality working but lacking production-grade features like management interfaces, observability, and comprehensive error handling. This plan outlines the path to production readiness across 4 phases over 6-8 weeks.

---

## Current State Assessment

### Strengths ✅
- **Clean architecture**: Layered separation (CLI → MCP → Core → Storage)
- **Lightweight**: Hash-based embeddings (no external LLM dependency)
- **Efficient search**: IVFFlat PostgreSQL index for fast vector retrieval
- **Docker-ready**: Working Dockerfile and docker compose for self-testing
- **MCP integration**: Ready for VS Code Copilot integration
- **CLI tools**: Commands for init, index, query with options for customization

### Critical Gaps ❌
1. **No management interface** - No way to view/manage indexed data
2. **No admin commands** - Cannot clear data, delete specific sources, etc.
3. **Minimal error handling** - Crashes on edge cases instead of graceful failures
4. **No observability** - Sparse logging, no metrics/tracing
5. **Credentials exposed** - Passwords in `copilot.mcp.json` and config files
6. **Insufficient testing** - Only 2 unit tests, zero integration tests
7. **No data model for topics/categories** - All data flat, no organization
8. **Single embedding generator** - Only hash-based, no swappable implementations
9. **No schema versioning** - Difficult to evolve database design
10. **Config validation missing** - No type safety on settings at startup

---

## Production Readiness Phases

### Phase 1: Foundation & Management (Weeks 1-2)

**Goal**: Enable management and observability

#### 1.1 Add Management Commands to CLI
- [ ] `delete --source <path>` - Remove documents by source path
- [ ] `purge-all` - Clear entire database with confirmation
- [ ] `stats` - Display index statistics (doc count, chunk count, total tokens)
- [ ] `list-sources` - Show all indexed sources with indexing date
- [ ] `reindex --source <path>` - Force re-index even if hash unchanged
- [ ] `health-check` - Verify database connectivity

**Files to modify**: `src/NebulaRAG.Cli/Program.cs`
**New files**: `src/NebulaRAG.Core/Services/RagManagementService.cs`

#### 1.2 Implement Structured Logging
- [ ] Add `ILogger<T>` to all services
- [ ] Replace `Console.WriteLine` with structured logging
- [ ] Support JSON-formatted logs for production
- [ ] Add log levels: DEBUG, INFO, WARN, ERROR
- [ ] Log key operations: indexing start/end, query execution, schema ops

**Files to add**: 
- `src/NebulaRAG.Core/Services/LoggingExtensions.cs`
- Update all service constructors

**Dependencies**: Add `Microsoft.Extensions.Logging` NuGet packages

#### 1.3 Configuration Validation & Secrets Management
- [ ] Add `IValidatableObject` to `RagSettings`
- [ ] Validate database connection on startup
- [ ] Support `.env` files for secrets (never commit `.env`)
- [ ] Update `.gitignore`: `ragsettings.local.json`, `.env`, `*.env`
- [ ] Add config loading order: defaults → JSON → env vars → `.env`
- [ ] Secure MCP container credential handling

**Files to modify**: 
- `src/NebulaRAG.Core/Configuration/RagSettings.cs`
- Update `Program.cs` in CLI and MCP

#### 1.4 Enhanced Error Handling
- [ ] Create `RagException` base class with error codes
- [ ] Handle database connection failures gracefully
- [ ] Validate inputs with meaningful error messages
- [ ] Add retry logic for transient database errors
- [ ] Log full exception context (stack trace only in DEBUG)

**Files to add**: `src/NebulaRAG.Core/Exceptions/RagException.cs`

#### 1.5 Update MCP Server with Management Tools
- [ ] `rag_delete_source` - Remove documents by source
- [ ] `rag_purge_all` - Clear database
- [ ] `rag_list_sources` - Show indexed sources
- [ ] `rag_get_stats` - Statistics endpoint
- [ ] `rag_health_check` - Enhanced health reporting

**Files to modify**: `src/NebulaRAG.Mcp/Program.cs`

---

### Phase 2: Admin UI & Topic Organization (Weeks 3-4)

**Goal**: Visual management and data organization

#### 2.1 Extend Schema for Topics/Categories
```sql
-- Add topics table and relationships
ALTER TABLE rag_documents ADD COLUMN topics TEXT[] DEFAULT '{}';
ALTER TABLE rag_chunks ADD COLUMN topic_id BIGINT;
CREATE TABLE rag_topics (
    id BIGSERIAL PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    description TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);
ALTER TABLE rag_chunks ADD FOREIGN KEY (topic_id) REFERENCES rag_topics(id);
```

- [ ] Modify `PostgresRagStore.InitializeSchemaAsync()` to include topics table
- [ ] Add topic assignment during indexing (can be inferred from source path or manual)
- [ ] Update search to optionally filter by topic

**Files to modify**: 
- `src/NebulaRAG.Core/Storage/PostgresRagStore.cs`
- `src/NebulaRAG.Core/Services/RagIndexer.cs`
- `src/NebulaRAG.Core/Configuration/RagSettings.cs` (add topic mapping config)

#### 2.2 Create REST API for Admin Panel
- [ ] New `NebulaRAG.Api` project (minimal ASP.NET 10 API)
- [ ] Endpoints:
  - `GET /api/stats` - Index statistics
  - `GET /api/documents` - List all documents with paging
  - `GET /api/sources` - List sources
  - `DELETE /api/documents/{id}` - Delete document
  - `DELETE /api/purge` - Admin purge (requires secret)
  - `GET /api/topics` - List topics
  - `POST /api/topics` - Create topic
  - `GET /api/documents/{id}/chunks` - View document chunks
- [ ] Add CORS for admin UI origin
- [ ] API key or token authentication for management endpoints

**Files to create**: 
- `src/NebulaRAG.Api/Controllers/StatsController.cs`
- `src/NebulaRAG.Api/Controllers/DocumentsController.cs`
- `src/NebulaRAG.Api/Controllers/TopicsController.cs`
- `src/NebulaRAG.Api/Middleware/AuthenticationMiddleware.cs`

#### 2.3 Build Admin Web UI (React/Vue)
- [ ] New `admin-ui` folder with Node.js frontend
- [ ] Dashboard: Overall stats, recent indexing activity
- [ ] Documents view: Table with search, delete action
- [ ] Topics view: Manage topics, assign to documents
- [ ] Indexing logs: Recent operations, status
- [ ] Simple auth: API key input on login

**Implementation**: 
- Create with `npm create vite@latest admin-ui -- --template react`
- Use React Query for API calls
- Use Tailwind CSS for styling
- Store API key in `localStorage` (warn about security)

#### 2.4 Package and Deploy Admin UI
- [ ] Serve admin UI from API (serve static files from `/app/admin` route)
- [ ] OR: Deploy as separate container in compose
- [ ] Environment configuration for API base URL

---

### Phase 3: Quality & Resilience (Weeks 5-6)

**Goal**: Comprehensive testing, optimization, and hardening

#### 3.1 Comprehensive Integration Tests
- [ ] Test full indexing workflow with real PostgreSQL (via TestContainers)
- [ ] Test query accuracy with known documents
- [ ] Test concurrent operations (parallel indexing)
- [ ] Test schema migration scenarios
- [ ] Test config loading from multiple sources
- [ ] Test error cases: bad DB connection, invalid queries, etc.

**Files to create**: 
- `tests/NebulaRAG.Integration.Tests/` project
- `IndexingTests.cs` - End-to-end indexing
- `QueryTests.cs` - Query accuracy and ranking
- `ManagementTests.cs` - CRUD operations
- `ConfigurationTests.cs` - Settings validation

**Dependencies**: TestContainers, xUnit

#### 3.2 Add Performance Monitoring
- [ ] Track metrics: indexing time, query latency, chunk counts
- [ ] Add query execution timing to logs
- [ ] Add slow query detection (>1s queries)
- [ ] Database query profiling: Add indexes comments
- [ ] Memory usage monitoring in MCP server

**Files to add**: `src/NebulaRAG.Core/Diagnostics/PerformanceMonitor.cs`

#### 3.3 Security Hardening
- [ ] Never hardcode credentials anywhere
- [ ] Sanitize logs (no passwords in error messages)
- [ ] Add connection pooling settings validation
- [ ] Implement SQL injection prevention (already using parameterized queries, verify)
- [ ] Add rate limiting in API endpoints
- [ ] Consider TLS/SSL for PostgreSQL connections by default
- [ ] Remove credentials from `copilot.mcp.json` - use env vars only

**Files to modify**: All credential handling

#### 3.4 Optimize Database Queries
- [ ] Add index on `rag_documents.source_path` (already done, verify)
- [ ] Add index on `rag_chunks.document_id` (already done, verify)
- [ ] Analyze slow queries: check query performance in PostgreSQL
- [ ] Consider connection pooling (Npgsql connection string settings)
- [ ] Batch operations: bulk delete, bulk insert verification
- [ ] Add ANALYZE hints for the optimizer

**Files to review**: `src/NebulaRAG.Core/Storage/PostgresRagStore.cs`

#### 3.5 Add Backup & Recovery Strategy
- [ ] Document backup procedures (pg_dump script)
- [ ] Create `scripts/backup-rag-db.sh`
- [ ] Create `scripts/restore-rag-db.sh`
- [ ] Add schema versioning/migration system
- [ ] Document disaster recovery procedures

**Files to create**: 
- `scripts/backup-rag-db.ps1`
- `scripts/restore-rag-db.ps1`
- `scripts/migrate-schema.sql` (empty, template for future)

---

### Phase 4: Deployment & Documentation (Weeks 7-8)

**Goal**: Production deployment readiness and comprehensive docs

#### 4.1 Docker & Deployment Hardening
- [ ] Multi-stage Dockerfile optimization (already good)
- [ ] Non-root user in container (don't run as root)
- [ ] Health check scripts for both API and MCP
- [ ] Docker compose for full stack: db + api + admin-ui
- [ ] Environment variable documentation in compose
- [ ] Secrets management strategy (Azure Key Vault, 1Password, etc.)

**Files to modify**: 
- `Dockerfile` - Add user
- `compose.yaml` - Add api, admin-ui services, healthchecks
- Create `compose.prod.yaml` for production overrides

#### 4.2 Comprehensive Documentation
- [ ] **docs/ARCHITECTURE.md**: System design, component interactions
- [ ] **DEPLOYMENT.md**: Production deployment checklist
- [ ] **API.md**: REST API documentation (OpenAPI spec)
- [ ] **ADMIN_GUIDE.md**: Admin panel usage
- [ ] **TROUBLESHOOTING.md**: Common issues and solutions
- [ ] **CONFIG_REFERENCE.md**: All configuration options
- [ ] **SECURITY.md**: Security best practices and hardening
- [ ] **PERFORMANCE_TUNING.md**: PostgreSQL tuning, scaling
- [ ] **DEVELOPMENT.md**: Local development setup, testing

**Files to create**: All above Markdown files in `/docs` folder

#### 4.3 CI/CD Pipeline Setup
- [ ] GitHub Actions workflow for:
  - Build/test on PR
  - Security scanning (e.g., SonarQube, dependency audit)
  - Docker image build/push to registry
  - Integration tests against Docker PostgreSQL
  - Release tagging and version bumping
- [ ] Setup branch protection: require PR review + passing tests
- [ ] Automatic version tagging: package version synced with git tags

**Files to create**: `.github/workflows/ci-cd.yml`

#### 4.4 Production Checklist
- [ ] [ ] All tests passing (unit + integration)
- [ ] [ ] Zero compiler warnings
- [ ] [ ] Security audit completed
- [ ] [ ] Documentation complete and reviewed
- [ ] [ ] Performance tested and benchmarked
- [ ] [ ] Backup/recovery tested
- [ ] [ ] Deployment tested in staging
- [ ] [ ] Monitoring and alerting configured
- [ ] [ ] Incident response plan documented
- [ ] [ ] Release notes prepared

#### 4.5 Monitoring & Observability Setup
- [ ] Application logging to file/centralized logging (e.g., ELK, CloudWatch)
- [ ] Database monitoring (slow queries, connection count)
- [ ] Alerting thresholds: high latency, database errors, indexing failures
- [ ] Dashboard for production metrics
- [ ] Error tracking (e.g., Sentry)

---

## Implementation Priority Matrix

| Task | Priority | Phase | Effort | Impact |
|------|----------|-------|--------|--------|
| Management commands (delete, purge, stats) | **Critical** | 1 | L | H |
| Structured logging | **Critical** | 1 | M | H |
| Configuration validation | **Critical** | 1 | M | H |
| Credentials security | **Critical** | 1 | M | H |
| Integration tests | **High** | 3 | L | H |
| Admin UI | **High** | 2 | XL | H |
| REST API for admin | **High** | 2 | L | H |
| Topic/category support | **High** | 2 | M | M |
| Error handling improvement | **High** | 1 | M | H |
| Performance optimization | **Medium** | 3 | M | M |
| Backup strategy | **Medium** | 3 | S | M |
| Docker/deployment hardening | **Medium** | 4 | M | M |
| Documentation | **Medium** | 4 | XL | M |
| CI/CD pipeline | **Low** | 4 | L | L |

---

## Technology Stack Recommendations

### Phase 1-2 Additions
- **Logging**: `Microsoft.Extensions.Logging` + `Serilog` (JSON output)
- **Validation**: `FluentValidation` (cleaner than `IValidatableObject`)
- **Secrets**: `Microsoft.Extensions.Configuration.UserSecrets` (dev) + env vars (prod)

### Phase 2 Additions (Admin API & UI)
- **API Framework**: ASP.NET Core 10 Minimal APIs
- **CORS**: Built-in CORS middleware
- **Auth**: API Key authentication (simple) or JWT (better)
- **Frontend**: React 18 + Vite + TypeScript + Tailwind
- **HTTP Client**: React Query (TanStack Query)

### Phase 3 Additions
- **Integration Tests**: TestContainers + xUnit
- **Benchmarking**: BenchmarkDotNet
- **Profiling**: MiniProfiler for SQL queries

### Phase 4 Additions
- **CI/CD**: GitHub Actions
- **Container Registry**: Docker Hub or GitHub Container Registry
- **Monitoring**: Prometheus + Grafana (or DataDog, New Relic)
- **Error Tracking**: Sentry

---

## Success Metrics

By end of Phase 4, the following should be true:

✅ **Reliability**: 99.9% uptime in staging, zero unhandled exceptions in logs  
✅ **Observability**: All operations logged, metrics dashboards available  
✅ **Manageability**: Can view/manage all data via UI, no direct DB access needed  
✅ **Security**: No plaintext credentials in repos, API key protected management endpoints  
✅ **Performance**: Query latency <100ms for 100K documents, indexing >1K chunks/sec  
✅ **Testing**: >80% code coverage, all critical paths integration tested  
✅ **Documentation**: New user can deploy and operate within 1 hour  
✅ **Deployment**: Production deployment automated via CI/CD  

---

## Risk Mitigation

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|-----------|
| Schema changes break existing data | Medium | High | Add schema versioning + migration scripts in Phase 3 |
| API performance bottleneck | Medium | Medium | Load test with realistic query volumes in Phase 3 |
| Credentials leak in logs | Low | Critical | Implement credential masking in Phase 1 logging |
| PostgreSQL connection pool exhaustion | Low | High | Test with concurrent clients, set pool limits in Phase 3 |
| Admin UI becomes maintenance burden | Low | Medium | Keep UI simple, use off-the-shelf components |

---

## Timeline

```
Week 1-2: Phase 1 (Management & Logging)
  ├─ Management commands
  ├─ Structured logging
  ├─ Config validation
  └─ Error handling

Week 3-4: Phase 2 (Admin UI & Topics)
  ├─ Schema extensions
  ├─ REST API
  └─ Admin UI

Week 5-6: Phase 3 (Quality & Resilience)
  ├─ Integration tests
  ├─ Performance monitoring
  ├─ Security hardening
  └─ Backup strategy

Week 7-8: Phase 4 (Deployment & Docs)
  ├─ Docker hardening
  ├─ Documentation
  ├─ CI/CD setup
  └─ Production checklist
```

---

## Next Steps

1. **Review & Approve**: Discuss this plan with stakeholders
2. **Prioritize**: Confirm Phase 1 tasks align with your goals
3. **Create Issues**: Break down Phase 1 tasks into GitHub issues
4. **Assign Owners**: Determine who works on which tasks
5. **Set Milestones**: Create GitHub milestones for each phase
6. **Begin Phase 1**: Start with management commands + logging

---

## References

- [NebulaRAG README](./README.md)
- PostgreSQL pgvector: https://github.com/pgvector/pgvector
- MCP Specification: https://modelcontextprotocol.io/
- ASP.NET Core Best Practices: https://learn.microsoft.com/en-us/aspnet/core/
- .NET Security Best Practices: https://learn.microsoft.com/en-us/dotnet/standard/security/

