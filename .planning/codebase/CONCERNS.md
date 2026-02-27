# Codebase Concerns

**Analysis Date:** 2026-02-27

## Tech Debt

**RagManagementService Instantiation:**
- Issue: Multiple RagManagementService instances created with same dependencies across different entry points (CLI, MCP, AddonHost)
- Files: `src/NebulaRAG.Cli/Program.cs` (lines 141, 157, 180, 201, 216), `src/NebulaRAG.Mcp/Program.cs` (line 36), `src/NebulaRAG.AddonHost/Program.cs` (line 53)
- Impact: Potential memory leaks, duplicate resource usage, violates DRY principle
- Fix approach: Create service registration in dependency injection container, or create shared factory

**PostgresRagStore Class Size:**
- Issue: Large class with 1574 lines, likely violates single responsibility principle
- Files: `src/NebulaRAG.Core/Storage/PostgresRagStore.cs`
- Impact: Hard to maintain, test, and modify; high cognitive load
- Fix approach: Split into focused classes (DocumentRepository, ChunkRepository, VectorSearchService, SchemaManager)

**TODO Comment in CLI:**
- Issue: Pending todo for vector database implementation
- Files: `src/NebulaRAG.Cli/Program.cs` (line 384)
- Impact: Incomplete feature implementation blocking user-facing documentation
- Fix approach: Remove or replace with clear "Coming Soon" placeholder when feature is added

## Known Bugs

**Empty Return Patterns:**
- Issue: Multiple places return null, empty arrays, or empty objects instead of proper error handling
- Files: `src/NebulaRAG.Core/Pathing/SourcePathNormalizer.cs`, `src/NebulaRAG.Core/Storage/PostgresRagStore.cs`, `src/NebulaRAG.Mcp/Program.cs`, `src/NebulaRAG.Core/Chunking/TextChunker.cs`
- Symptoms: Null reference exceptions, unexpected empty results
- Trigger: When normalization, database operations, or chunking fail
- Workaround: Wrap all calls with null checks

**Invalid Resource Usage:**
- Issue: Using minified logger instances for database operations without proper disposal
- Files: `src/NebulaRAG.Cli/Program.cs` (lines 141, 157, 180, 201, 216)
- Symptoms: Potential resource leaks
- Trigger: Long-running CLI operations
- Workaround: Use ILogger<ClassName> pattern instead of factory-created loggers

## Security Considerations

**Query Results Sanitization:**
- Risk: Potential XSS or injection attacks through displayed query results
- Files: `src/NebulaRAG.Cli/Program.cs` (lines 124-132)
- Current mitigation: Basic truncation to 220 characters
- Recommendations: Implement proper HTML escaping, add input validation for query text

**File System Access:**
- Risk: Unrestricted file system access for indexing
- Files: `src/NebulaRAG.Cli/Program.cs` (line 85)
- Current mitigation: Uses current directory when no source specified
- Recommendations: Validate file paths, prevent directory traversal, add access controls

**Database Connection String Exposure:**
- Risk: Connection strings may be logged or exposed in error messages
- Files: `src/NebulaRAG.Core/Storage/PostgresRagStore.cs`
- Current mitigation: Basic validation on initialization
- Recommendations: Mask connection strings in logs, use connection pooling

## Performance Bottlenecks

**No Result Caching:**
- Problem: Query results not cached, repeated queries hit database
- Files: `src/NebulaRAG.Core/Services/RagQueryService.cs`
- Cause: No caching layer implemented
- Improvement path: Add Redis cache for query results, implement cache invalidation

**Sequential Chunk Processing:**
- Problem: Documents processed sequentially during indexing
- Files: `src/NebulaRAG.Core/Services/RagIndexer.cs`
- Cause: No parallelization implemented
- Improvement path: Parallelize document processing, batch database inserts

**Large File Loading:**
- Problem: Entire PostgresRagStore class loaded into memory (81KB)
- Files: `src/NebulaRAG.Core/Storage/PostgresRagStore.cs`
- Cause: Single large implementation file
- Improvement path: Split into smaller focused classes

## Fragile Areas

**Hard-coded Configuration Loading:**
- Why fragile: Multiple hardcoded paths for configuration file resolution
- Files: `src/NebulaRAG.Cli/Program.cs` (lines 320-341)
- Safe modification: Extract configuration search strategy to separate service
- Test coverage: Unit tests for different config scenarios

**MCP Transport Handler:**
- Why fragile: Large class with 862 lines handling multiple MCP protocol details
- Files: `src/NebulaRAG.Core/Mcp/McpTransportHandler.Tools.cs`
- Safe modification: Split into smaller handlers by tool category
- Test coverage: Integration tests for MCP protocol compliance

## Scaling Limits

**Single Database Instance:**
- Current capacity: Limited by PostgreSQL single instance
- Limit: No horizontal scaling for read/write operations
- Scaling path: Implement read replicas, add connection pooling, consider sharding

**Memory Usage:**
- Current capacity: Dependent on available system memory
- Limit: Large document batches may cause OOM
- Scaling path: Implement streaming processing, add memory monitoring

## Dependencies at Risk

**Hash Embedding Generator:**
- Risk: Basic hash-based embeddings, not semantic
- Impact: Poor search quality, prevents future AI features
- Migration plan: Replace with OpenAI embeddings or semantic embedding model

**No Integration Testing:**
- Risk: Limited testing of database operations and service interactions
- Impact: Database schema changes may break multiple services
- Migration plan: Add integration tests for database operations

## Missing Critical Features

**Authentication and Authorization:**
- Problem: No user authentication or access control
- Blocks: Multi-tenant deployments, secure data handling

**Monitoring and Observability:**
- Problem: Basic logging only, no metrics or tracing
- Blocks: Performance optimization, issue detection

## Test Coverage Gaps

**Service Layer Testing:**
- What's not tested: RagIndexer, RagQueryService integration scenarios
- Files: `src/NebulaRAG.Core/Services/RagIndexer.cs`, `src/NebulaRAG.Core/Services/RagQueryService.cs`
- Risk: Database connection failures not properly tested
- Priority: High (database-dependent operations)

**Error Handling:**
- What's not tested: Database timeout, connection error scenarios
- Files: `src/NebulaRAG.Core/Storage/PostgresRagStore.cs`
- Risk: System may fail ungracefully under database issues
- Priority: Medium

---

*Concerns audit: 2026-02-27*