# Architecture

**Analysis Date:** 2026-02-27

## Pattern Overview

**Overall:** Clean Architecture with Onion Pattern and Domain-Centric Design

**Key Characteristics:**
- Domain-first design with clear separation of concerns
- SOLID principles enforcement through dependency inversion
- Multi-project .NET solution with cross-cutting concerns
- Component-based architecture with adapters for various entry points
- Embedded vector database for data persistence

## Layers

**Domain Layer:**
- Purpose: Core business logic and domain models
- Location: `src/NebulaRAG.Core/`
- Contains: Interfaces, domain models, core algorithms
- Depends on: .NET 10 base classes
- Used by: All other projects through dependency injection

**Application Layer:**
- Purpose: Service orchestration and business rule coordination
- Location: `src/NebulaRAG.Core/Services/`
- Contains: `RagQueryService`, `RagManagementService`, `RagIndexer`
- Depends on: Domain layer, external storage, configuration
- Used by: CLI, AddonHost, MCP projects

**Infrastructure Layer:**
- Purpose: External adapters and technical implementations
- Location: `src/NebulaRAG.Core/`
  - `Storage/PostgresRagStore` - PostgreSQL with vector extension
  - `Embeddings/HashEmbeddingGenerator` - Hash-based embeddings
  - `Chunking/TextChunker` - Text processing utilities
  - `Configuration/RagSettings` - Configuration management
  - `Mcp/` - MCP transport implementation
  - `Exceptions/` - Custom exception hierarchy
- Depends on: Domain layer interfaces, PostgreSQL client
- Used by: Application layer services

**Presentation Layer:**
- Purpose: Entry points and user interfaces
  - **CLI (`NebulaRAG.Cli`)**: Command-line interface for RAG operations
  - **Web API (`NebulaRAG.AddonHost`)**: ASP.NET Core dashboard with REST API
  - **MCP Server (`NebulaRAG.Mcp`)**: Model Context Protocol server
  - **React Dashboard (`dashboard/`)**: TypeScript/React web interface
- Depends on: Application layer services
- Used by: End users and other systems

## Data Flow

**Query Flow:**
1. User request → Presentation layer (CLI/HTTP/MCP)
2. Service registration via dependency injection
3. `RagQueryService` orchestrates query operation
4. `PostgresRagStore` retrieves embeddings and documents
5. `HashEmbeddingGenerator` processes embeddings
6. Results returned to presentation layer

**Indexing Flow:**
1. Source document → Presentation layer
2. `RagIndexer` coordinates ingestion pipeline
3. `TextChunker` processes documents into chunks
4. `HashEmbeddingGenerator` generates embeddings
5. `PostgresRagStore` stores vectors and metadata
6. Manifest update via `RagSourcesManifestService`

**State Management:**
- PostgreSQL with pgvector extension for vector storage
- In-memory caching for frequently accessed metadata
- Settings management through configuration system
- OpenTelemetry for observability metrics

## Key Abstractions

**IRuntimeTelemetrySink:**
- Purpose: Telemetry and monitoring abstraction
- Examples: `[src/NebulaRAG.Core/Services/IRuntimeTelemetrySink.cs]`
- Pattern: Strategy pattern with null object pattern

**IEmbeddingGenerator:**
- Purpose: Vector generation abstraction
- Examples: `[src/NebulaRAG.Core/Embeddings/IEmbeddingGenerator.cs]`
- Pattern: Strategy pattern for embedding algorithms

**RagException hierarchy:**
- Purpose: Domain-specific error handling
- Examples: `[src/NebulaRAG.Core/Exceptions/RagException.cs]`
- Pattern: Custom exception inheritance

**McpTransportHandler:**
- Purpose: JSON-RPC protocol handling
- Examples: `[src/NebulaRAG.Core/Mcp/McpTransportHandler.cs]`
- Pattern: Adapter pattern for protocol translation

## Entry Points

**CLI Application:**
- Location: `[src/NebulaRAG.Cli/Program.cs]`
- Triggers: Command line arguments (`index`, `query`, `list`, `stats`)
- Responsibilities: Console-based RAG operations

**Web API:**
- Location: `[src/NebulaRAG.AddonHost/Program.cs]`
- Triggers: HTTP requests on `/dashboard/` and `/mcp` endpoints
- Responsibilities: REST API for dashboard and MCP server

**MCP Server:**
- Location: `[src/NebulaRAG.Mcp/Program.cs]`
- Triggers: JSON-RPC messages via stdio
- Responsibilities: Model Context Protocol implementation

**Web Dashboard:**
- Location: `[dashboard/src/main.tsx]`
- Triggers: User interactions through React UI
- Responsibilities: Visualization and interaction with RAG system

## Error Handling

**Strategy:** Exception hierarchy with domain-specific error codes

**Patterns:**
- `RagException` - Base class with error code
- `RagDatabaseException` - Database operation failures
- `RagConfigurationException` - Invalid configuration
- `RagIndexingException` - Document processing failures
- `RagQueryException` - Query operation failures
- Structured error responses with HTTP status codes

## Cross-Cutting Concerns

**Logging:** Serilog with structured JSON output
**Validation:** Fluent validation through settings validation methods
**Authentication:** Basic JWT-based authentication (not yet implemented)
**Configuration:** JSON files with environment variable overrides
**Observability:** OpenTelemetry integration for traces and metrics

---

*Architecture analysis: 2026-02-27*