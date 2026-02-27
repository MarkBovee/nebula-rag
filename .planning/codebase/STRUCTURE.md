# Codebase Structure

**Analysis Date:** 2026-02-27

## Directory Layout

```
nebula-rag/
├── src/                          # Source code
│   ├── NebulaRAG.Core/          # Core library
│   │   ├── Chunking/            # Text processing
│   │   ├── Configuration/       # Settings management
│   │   ├── Embeddings/          # Vector generation
│   │   ├── Exceptions/          # Error handling
│   │   ├── Mcp/                 # MCP protocol
│   │   ├── Models/              # Data models
│   │   ├── Pathing/             # Path utilities
│   │   ├── Services/            # Business logic
│   │   └── Storage/             # Data persistence
│   ├── NebulaRAG.Cli/           # Command-line interface
│   ├── NebulaRAG.AddonHost/     # Web API host
│   │   ├── Controllers/         # API controllers
│   │   ├── Services/            # Dashboard services
│   │   └── wwwroot/             # Static files
│   └── NebulaRAG.Mcp/           # MCP server host
├── dashboard/                   # React web application
│   ├── src/
│   │   ├── api/                 # API client code
│   │   ├── components/          # React components
│   │   ├── monitoring/          # Error monitoring
│   │   ├── styles/              # CSS/SCSS files
│   │   ├── types/               # TypeScript types
│   │   └── utils/               # Utility functions
│   └── tests/                   # Dashboard tests
├── tests/                       # Unit tests
│   └── NebulaRAG.Tests/         # Test project
├── scripts/                     # Build and deployment scripts
├── .planning/                   # Project planning documents
├── .github/                     # GitHub workflows and skills
├── container/                   # Docker container resources
├── NebulaRAG.sln               # Solution file
├── README.md                    # Project documentation
├── compose.yaml                 # Docker Compose
└── Dockerfile                   # Container definition
```

## Directory Purposes

**src/NebulaRAG.Core/**:
- Purpose: Core library containing domain logic
- Contains: Services, models, interfaces, utilities
- Key files: `[src/NebulaRAG.Core/Services/RagQueryService.cs]`

**src/NebulaRAG.Cli/**:
- Purpose: Command-line interface
- Contains: CLI application logic
- Key files: `[src/NebulaRAG.Cli/Program.cs]`

**src/NebulaRAG.AddonHost/**:
- Purpose: Web API hosting
- Contains: REST controllers and static file serving
- Key files: `[src/NebulaRAG.AddonHost/Program.cs]`

**src/NebulaRAG.Mcp/**:
- Purpose: MCP server hosting
- Contains: Protocol transport implementation
- Key files: `[src/NebulaRAG.Mcp/Program.cs]`

**dashboard/**:
- Purpose: Web frontend for the RAG system
- Contains: React application and dashboard interface
- Key files: `[dashboard/src/main.tsx]`, `[dashboard/src/App.tsx]`

**tests/**:
- Purpose: Unit and integration tests
- Contains: Test projects
- Key files: `[tests/NebulaRAG.Tests/]`

## Key File Locations

**Entry Points:**
- `[src/NebulaRAG.Cli/Program.cs]`: CLI entry point
- `[src/NebulaRAG.AddonHost/Program.cs]`: Web API entry point
- `[src/NebulaRAG.Mcp/Program.cs]`: MCP server entry point
- `[dashboard/src/main.tsx]`: React app entry point

**Configuration:**
- `[src/NebulaRAG.Core/Configuration/RagSettings.cs]`: Settings definition
- `[src/NebulaRAG.Core/Models/ManagementModels.cs]`: API models
- `[src/NebulaRAG.Core/Models/MemoryModels.cs]`: Memory models

**Core Logic:**
- `[src/NebulaRAG.Core/Services/RagQueryService.cs]`: Query execution
- `[src/NebulaRAG.Core/Services/RagIndexer.cs]`: Document indexing
- `[src/NebulaRAG.Core/Storage/PostgresRagStore.cs]`: Database operations
- `[src/NebulaRAG.Core/Embeddings/HashEmbeddingGenerator.cs]`: Embedding generation

**Testing:**
- `[tests/NebulaRAG.Tests/]`: Unit test project
- `[dashboard/tests/]`: Frontend tests

## Naming Conventions

**Files:**
- PascalCase for C# classes and interfaces
- PascalCase for TypeScript components and utilities
- camelCase for local variables and method parameters
- kebab-case for directories (when used)

**Classes:**
- Service classes: `RagQueryService`
- Interface classes: `IEmbeddingGenerator`
- Model classes: `RagIndexStats`
- Exception classes: `RagDatabaseException`
- Controller classes: `RagApiController`

**Methods:**
- Public methods: `QueryAsync`, `IndexDocumentAsync`
- Private methods: `ValidateSettings`, `NormalizePath`
- Static methods: `BuildConnectionString`

**Variables:**
- Local variables: `queryResults`, `connectionString`
- Parameters: `cancellationToken`, `embeddingGenerator`
- Loop variables: `document`, `chunk`

## Where to Add New Code

**New Feature:**
- Core logic: Add to `src/NebulaRAG.Core/Services/`
- Models: Add to `src/NebulaRAG.Core/Models/`
- Tests: Add to `tests/NebulaRAG.Tests/`

**New Component/Module:**
- Backend API: Add controller in `src/NebulaRAG.AddonHost/Controllers/`
- Frontend component: Add to `dashboard/src/components/`
- MCP tool: Implement in `src/NebulaRAG.Core/Mcp/`

**Utilities:**
- Shared helpers: Add to `src/NebulaRAG.Core/`
- Frontend utilities: Add to `dashboard/src/utils/`
- CLI commands: Add to `src/NebulaRAG.Cli/Program.cs`

## Special Directories

**.github/**:
- Purpose: GitHub Actions workflows and AI skills
- Contains: CI/CD pipelines and AI agent instructions
- Generated: No, maintained manually

**.planning/**:
- Purpose: Project planning and documentation
- Contains: Architecture and quality documents
- Generated: Yes, by GSD tools

**container/**:
- Purpose: Docker container resources
- Contains: Container build assets
- Generated: No, maintained manually

**dashboard/src/monitoring/**:
- Purpose: Frontend error tracking
- Contains: Error boundary and monitoring tools
- Generated: No, maintained manually

---

*Structure analysis: 2026-02-27*