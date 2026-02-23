#!/usr/bin/env bash
set -euo pipefail

OPTIONS_FILE="/data/options.json"

if [ ! -f "${OPTIONS_FILE}" ]; then
  echo "[nebula-rag] Missing ${OPTIONS_FILE}."
  exit 1
fi

operation="$(jq -r '.operation // "init"' "${OPTIONS_FILE}")"
source_path="$(jq -r '.source_path // "/share"' "${OPTIONS_FILE}")"
query_text="$(jq -r '.query_text // ""' "${OPTIONS_FILE}")"
query_limit="$(jq -r '.query_limit // 5' "${OPTIONS_FILE}")"
config_path="$(jq -r '.config_path // ""' "${OPTIONS_FILE}")"

db_host="$(jq -r '.database.host // ""' "${OPTIONS_FILE}")"
db_port="$(jq -r '.database.port // 5432' "${OPTIONS_FILE}")"
db_name="$(jq -r '.database.name // ""' "${OPTIONS_FILE}")"
db_username="$(jq -r '.database.username // ""' "${OPTIONS_FILE}")"
db_password="$(jq -r '.database.password // ""' "${OPTIONS_FILE}")"
db_ssl_mode="$(jq -r '.database.ssl_mode // "Prefer"' "${OPTIONS_FILE}")"

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

command=("dotnet" "NebulaRAG.Cli.dll")

case "${operation}" in
  init)
    command+=("init")
    ;;
  index)
    command+=("index" "--source" "${source_path}")
    ;;
  query)
    if [ -z "${query_text}" ]; then
      echo "[nebula-rag] query_text is required when operation=query."
      exit 1
    fi
    command+=("query" "--text" "${query_text}" "--limit" "${query_limit}")
    ;;
  stats)
    command+=("stats")
    ;;
  list-sources)
    command+=("list-sources")
    ;;
  health-check)
    command+=("health-check")
    ;;
  *)
    echo "[nebula-rag] Unsupported operation: ${operation}."
    exit 1
    ;;
esac

if [ -n "${config_path}" ]; then
  command+=("--config" "${config_path}")
fi

echo "[nebula-rag] Starting operation '${operation}'..."
"${command[@]}"
echo "[nebula-rag] Operation '${operation}' completed."
