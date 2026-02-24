#!/usr/bin/env bash
set -euo pipefail

OPTIONS_FILE="/data/options.json"

if [ ! -f "${OPTIONS_FILE}" ]; then
  echo "[nebula-rag] Missing ${OPTIONS_FILE}."
  exit 1
fi

db_host="$(jq -r '.database.host // ""' "${OPTIONS_FILE}")"
db_port="$(jq -r '.database.port // 5432' "${OPTIONS_FILE}")"
db_name="$(jq -r '.database.name // ""' "${OPTIONS_FILE}")"
db_username="$(jq -r '.database.username // ""' "${OPTIONS_FILE}")"
db_password="$(jq -r '.database.password // ""' "${OPTIONS_FILE}")"
db_ssl_mode="$(jq -r '.database.ssl_mode // "Prefer"' "${OPTIONS_FILE}")"
default_index_path="$(jq -r '.default_index_path // "/share"' "${OPTIONS_FILE}")"
path_base="$(jq -r '.path_base // ""' "${OPTIONS_FILE}")"

if [ -z "${db_password}" ]; then
  echo "[nebula-rag] database.password is required."
  exit 1
fi

export NEBULARAG_Database__Host="${db_host}"
export NEBULARAG_Database__Port="${db_port}"
export NEBULARAG_Database__Database="${db_name}"
export NEBULARAG_Database__Username="${db_username}"
export NEBULARAG_Database__Password="${db_password}"
export NEBULARAG_Database__SslMode="${db_ssl_mode}"
export NEBULARAG_DefaultIndexPath="${default_index_path}"
export NEBULARAG_PathBase="${path_base}"
export ASPNETCORE_URLS="http://0.0.0.0:8099"

if [ -f "/app/nebularag_commit.txt" ]; then
  echo "[nebula-rag] Image source commit: $(cat /app/nebularag_commit.txt)"
fi

echo "[nebula-rag] Starting Nebula RAG Add-on Host (Web UI + MCP endpoint)..."
exec dotnet NebulaRAG.AddonHost.dll
