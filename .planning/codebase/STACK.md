# Technology Stack

**Analysis Date:** 2026-02-27

## Languages

**Primary:**
- C# 10.0 - Backend API, MCP server, CLI tools, and core services in `src/NebulaRAG.*` projects

**Secondary:**
- TypeScript 5.4.5 - React dashboard frontend in `dashboard/` directory

## Runtime

**Environment:**
- .NET 10.0 - Runtime framework for all C# components
- Node.js 18+ - Runtime for dashboard development server

**Package Manager:**
- NuGet 6+ - C# package management
- npm 9+ - JavaScript/TypeScript package management

## Frameworks

**Core:**
- ASP.NET Core 10.0 - Web API server (`NebulaRAG.AddonHost`)
- React 18.3.1 - Dashboard UI (`dashboard/`)
- Vite 5.1.3 - Build tool for dashboard

**Testing:**
- Playwright 1.51.1 - E2E testing for dashboard
- .NET Unit Testing Framework - Backend unit tests (`NebulaRAG.Tests`)

**Build/Dev:**
- Docker - Containerized deployment with `compose.yaml`
- GitHub Actions - CI/CD workflows in `.github/workflows/`

## Key Dependencies

**Critical:**
- `Npgsql` 10.0.1 - PostgreSQL client
- `Pgvector` 0.3.2 - PostgreSQL pgvector extension client for vector search
- `Serilog` 4.1.0 - Structured logging
- `OpenTelemetry` 1.12.0 - Application telemetry and metrics
- `Recharts` 2.12.7 - Charting library for dashboard
- `Axios` 1.7.7 - HTTP client for dashboard

**Infrastructure:**
- `Microsoft.Extensions.*` - Configuration, dependency injection, logging abstractions
- `Microsoft.AspNetCore.*` - Web framework components

## Configuration

**Environment:**
- JSON configuration files (`ragsettings.json`, `ragsettings.local.json`, `ragsettings.container.json`)
- Environment variable support for runtime settings
- Configuration sections: `Database`, `Ingestion`, `Retrieval`

**Build:**
- `.csproj` files define dependencies and build options
- `vite.config.ts` for frontend build configuration
- `compose.yaml` for Docker services

## Platform Requirements

**Development:**
- .NET 10.0 SDK
- Node.js 18+ and npm
- Docker and Docker Compose
- PostgreSQL 16 with pgvector extension

**Production:**
- .NET 10.0 runtime
- PostgreSQL 16 with pgvector extension
- Docker container deployment

---

*Stack analysis: 2026-02-27*
```