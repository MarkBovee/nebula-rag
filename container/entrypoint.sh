#!/bin/sh
set -eu

exec dotnet /app/NebulaRAG.Mcp.dll "$@"
