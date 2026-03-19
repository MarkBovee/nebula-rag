#!/usr/bin/env bash
set -euo pipefail

MODE="Both"
TARGET_PATH=""
CLIENT_TARGETS="Both"
COPILOT_CONFIG_PATH=""
CLAUDE_USER_CONFIG_PATH=""
CLAUDE_PROJECT_CONFIG_PATH=""
CLAUDE_SETTINGS_PATH=""
CHANNEL="Auto"
SERVER_NAME="nebula-rag"
IMAGE_NAME="localhost/nebula-rag-mcp:latest"
TEMPLATE_RAW_BASE_URL="https://raw.githubusercontent.com/MarkBovee/NebulaRAG/main"
HOMEASSISTANT_MCP_URL="http://homeassistant.local:8099/nebula/mcp"
EXTERNAL_HOMEASSISTANT_MCP_URL=""
USE_EXTERNAL_HOMEASSISTANT_URL=0
FORCE_EXTERNAL=0
INSTALL_TARGET="Ask"
ENV_FILE_PATH="${HOME:-}/.nebula-rag/.env"
CREATE_ENV_TEMPLATE=0
SKIP_SKILL=0
NO_BACKUP=0
FORCE=0

usage() {
  cat <<'USAGE'
Usage: setup-nebula-rag.sh [options]

Options:
  --mode <Both|User|Project>
  --target-path <path>
  --client-targets <Both|Copilot|ClaudeCode|VSCode>
  --copilot-config-path <path>
  --user-config-path <path>           Alias for --copilot-config-path
  --claude-user-config-path <path>
  --claude-user-config <path>         Alias for --claude-user-config-path
  --claude-project-config-path <path>
  --claude-settings-path <path>
  --channel <Auto|Code|Insiders|Both> Compatibility flag; no longer changes behavior
  --server-name <name>
  --image-name <image>
  --template-raw-base-url <url>
  --home-assistant-mcp-url <url>
  --external-home-assistant-mcp-url <url>
  --use-external-home-assistant-url
  --force-external
  --install-target <Ask|LocalContainer|HomeAssistantAddon>
  --env-file-path <path>
  --create-env-template
  --skip-skill
  --no-backup
  --force
  -h, --help
USAGE
}

warn() {
  printf 'Warning: %s\n' "$*" >&2
}

normalize_shell_script_line_endings() {
  local path="$1"
  if [[ "${path##*.}" != "sh" || ! -f "$path" ]]; then
    return
  fi

  python3 - "$path" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
content = path.read_text(encoding="utf-8")
normalized = content.replace("\r\n", "\n").replace("\r", "\n")
if normalized != content:
    path.write_text(normalized, encoding="utf-8", newline="\n")
PY
}

ensure_directory() {
  local path="$1"
  mkdir -p "$path"
}

backup_file() {
  local path="$1"
  if [[ "$NO_BACKUP" -eq 1 || ! -f "$path" ]]; then
    return
  fi

  local timestamp
  timestamp="$(date +%Y%m%d%H%M%S)"
  cp "$path" "$path.$timestamp.bak"
  printf 'Backed up existing file to: %s.%s.bak\n' "$path" "$timestamp"
}

command_exists() {
  command -v "$1" >/dev/null 2>&1
}

require_python() {
  if ! command_exists python3; then
    echo "python3 is required for scripts/setup-nebula-rag.sh" >&2
    exit 1
  fi
}

resolve_template_file() {
  local template_root="$1"
  local relative_path="$2"
  local raw_base_url="$3"
  local required="$4"
  local local_path="$template_root/$relative_path"

  if [[ -f "$local_path" ]]; then
    python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$local_path"
    return
  fi

  if [[ -z "$raw_base_url" ]]; then
    if [[ "$required" == "1" ]]; then
      echo "Required template file not found locally and no raw template base URL provided: $relative_path" >&2
      exit 1
    fi

    return
  fi

  local normalized_relative_path="${relative_path#./}"
  normalized_relative_path="${normalized_relative_path#/}"
  local download_root="${TMPDIR:-/tmp}/nebula-rag-setup-template"
  local download_path="$download_root/$normalized_relative_path"

  if [[ ! -f "$download_path" ]]; then
    ensure_directory "$(dirname "$download_path")"
    local download_url="${raw_base_url%/}/$normalized_relative_path"
    if command_exists curl; then
      curl -fsSL "$download_url" -o "$download_path"
    elif command_exists wget; then
      wget -qO "$download_path" "$download_url"
    else
      echo "curl or wget is required to download template file: $download_url" >&2
      exit 1
    fi
    printf 'Downloaded template file: %s\n' "$normalized_relative_path"
  fi

  python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$download_path"
}

copy_file_safe() {
  local source="$1"
  local destination="$2"
  local force_write="$3"

  if [[ ! -f "$source" ]]; then
    echo "Source file not found: $source" >&2
    exit 1
  fi

  local source_abs destination_abs
  source_abs="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$source")"
  destination_abs="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$destination")"

  if [[ "$source_abs" == "$destination_abs" ]]; then
    normalize_shell_script_line_endings "$destination"
    printf 'Skip same source/destination: %s\n' "$destination"
    return
  fi

  if [[ -f "$destination" ]] && cmp -s "$source" "$destination"; then
    normalize_shell_script_line_endings "$destination"
    printf 'Skip unchanged file: %s\n' "$destination"
    return
  fi

  if [[ -f "$destination" && "$force_write" != "1" ]]; then
    normalize_shell_script_line_endings "$destination"
    printf 'Skip existing file: %s\n' "$destination"
    return
  fi

  ensure_directory "$(dirname "$destination")"
  cp "$source" "$destination"
  normalize_shell_script_line_endings "$destination"
  printf 'Copied file: %s\n' "$destination"
}

ensure_gitignore_entry() {
  local gitignore_path="$1"
  local entry="$2"

  if [[ ! -f "$gitignore_path" ]]; then
    printf '%s\n' "$entry" > "$gitignore_path"
    printf 'Wrote file: %s\n' "$gitignore_path"
    return
  fi

  if grep -Fxq "$entry" "$gitignore_path"; then
    printf 'Skip %s update: already present in %s\n' "$entry" "$gitignore_path"
    return
  fi

  printf '%s\n' "$entry" >> "$gitignore_path"
  printf 'Updated .gitignore with %s\n' "$entry"
}

resolve_homeassistant_mcp_url() {
  if [[ "$USE_EXTERNAL_HOMEASSISTANT_URL" -eq 1 ]]; then
    if [[ "$FORCE_EXTERNAL" -ne 1 ]]; then
      echo "External MCP URL mode is blocked by default for privacy. Re-run with --use-external-home-assistant-url --force-external --external-home-assistant-mcp-url <url> to opt in." >&2
      exit 1
    fi

    if [[ -z "$EXTERNAL_HOMEASSISTANT_MCP_URL" ]]; then
      echo "--use-external-home-assistant-url requires --external-home-assistant-mcp-url." >&2
      exit 1
    fi

    printf '%s\n' "$EXTERNAL_HOMEASSISTANT_MCP_URL"
    return
  fi

  printf '%s\n' "$HOMEASSISTANT_MCP_URL"
}

resolve_install_target() {
  if [[ "$INSTALL_TARGET" != "Ask" ]]; then
    printf '%s\n' "$INSTALL_TARGET"
    return
  fi

  if [[ ! -t 0 ]]; then
    printf 'HomeAssistantAddon\n'
    return
  fi

  printf '\nSelect NebulaRAG install target:\n'
  printf '  1) Home Assistant add-on (recommended)\n'
  printf '  2) Local container MCP (Podman env-file workflow)\n'

  while true; do
    read -r -p 'Enter 1 or 2 (default 1): ' selection
    if [[ -z "$selection" || "$selection" == "1" ]]; then
      printf 'HomeAssistantAddon\n'
      return
    fi

    if [[ "$selection" == "2" ]]; then
      printf 'LocalContainer\n'
      return
    fi

    printf 'Invalid selection. Enter 1 or 2.\n'
  done
}

validate_choice() {
  local value="$1"
  shift
  local option
  for option in "$@"; do
    if [[ "$value" == "$option" ]]; then
      return
    fi
  done

  echo "Invalid value: $value" >&2
  exit 1
}

resolve_client_targets() {
  local selected_targets="$1"
  local -n output_ref=$2
  output_ref=()

  case "$selected_targets" in
    Both)
      output_ref=("Copilot" "ClaudeCode")
      ;;
    VSCode)
      warn "VSCode is now treated as a compatibility alias for Copilot CLI. The installer writes Copilot CLI MCP config plus project-local hooks."
      output_ref=("Copilot")
      ;;
    Copilot|ClaudeCode)
      output_ref=("$selected_targets")
      ;;
    *)
      echo "Invalid client target: $selected_targets" >&2
      exit 1
      ;;
  esac

  if [[ "$CHANNEL" != "Auto" ]]; then
    warn "--channel is retained for compatibility but no longer changes setup behavior. Copilot CLI hooks are project-local and user-level MCP config lives in ~/.copilot/mcp-config.json."
  fi
}

array_contains() {
  local needle="$1"
  shift
  local item
  for item in "$@"; do
    if [[ "$item" == "$needle" ]]; then
      return 0
    fi
  done

  return 1
}

get_default_copilot_config_path() {
  local copilot_home="${COPILOT_HOME:-${HOME}/.copilot}"
  ensure_directory "$copilot_home"
  python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$copilot_home/mcp-config.json"
}

get_default_claude_user_config_path() {
  if [[ -z "${HOME:-}" ]]; then
    echo "HOME is not set. Provide --claude-user-config-path explicitly." >&2
    exit 1
  fi

  python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$HOME/.claude.json"
}

read_json_sanitize() {
  local path="$1"
  local default_json="$2"
  python3 - "$path" "$default_json" <<'PY'
import json, pathlib, re, sys

path = pathlib.Path(sys.argv[1])
default_json = sys.argv[2]

def sanitize(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"(?m)^\s*//.*$", "", text)
    text = re.sub(r",(\s*[}\]])", r"\1", text)
    return text

if not path.exists() or not path.read_text(encoding="utf-8").strip():
    print(default_json)
    raise SystemExit(0)

raw = path.read_text(encoding="utf-8")
try:
    obj = json.loads(raw)
except Exception:
    obj = json.loads(sanitize(raw))

print(json.dumps(obj))
PY
}

build_server_definition() {
  local selected_install_target="$1"
  local configured_homeassistant_mcp_url="$2"
  local configured_image="$3"
  local configured_env_file="$4"
  local host_source_path="$5"

  python3 - "$selected_install_target" "$configured_homeassistant_mcp_url" "$configured_image" "$configured_env_file" "$host_source_path" <<'PY'
import json
import sys

selected_install_target, configured_homeassistant_mcp_url, configured_image, configured_env_file, host_source_path = sys.argv[1:6]
if selected_install_target == "HomeAssistantAddon":
    print(json.dumps({"type": "http", "url": configured_homeassistant_mcp_url}, separators=(",", ":")))
    raise SystemExit(0)

args = [
    "run",
    "--rm",
    "-i",
    "--pull=never",
    "--memory=2g",
    "--cpus=1.0",
]
env = {}
if host_source_path:
    env["NEBULARAG_PathMappings"] = f"{host_source_path}=/workspace"
    args.extend([
        "--mount",
        f"type=bind,source={host_source_path},target=/workspace",
        "--workdir",
        "/workspace",
    ])
args.extend(["--env-file", configured_env_file, configured_image, "--skip-self-test"])
server = {"type": "stdio", "command": "podman", "args": args}
if env:
    server["env"] = env
print(json.dumps(server, separators=(",", ":")))
PY
}

upsert_mcp_server() {
  local path="$1"
  local configured_server_name="$2"
  local server_definition_json="$3"
  local force_write="$4"

  backup_file "$path"
  ensure_directory "$(dirname "$path")"

  python3 - "$path" "$configured_server_name" "$server_definition_json" "$force_write" <<'PY'
import json
import pathlib
import re
import sys

path = pathlib.Path(sys.argv[1])
server_name = sys.argv[2]
server_definition = json.loads(sys.argv[3])
force_write = sys.argv[4] == "1"

def sanitize(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"(?m)^\s*//.*$", "", text)
    text = re.sub(r",(\s*[}\]])", r"\1", text)
    return text

root = {"mcpServers": {}}
if path.exists():
    raw = path.read_text(encoding="utf-8")
    if raw.strip():
        try:
            root = json.loads(raw)
        except Exception:
            root = json.loads(sanitize(raw))

if not isinstance(root, dict):
    root = {}
if not isinstance(root.get("mcpServers"), dict):
    root["mcpServers"] = {}

if server_name in root["mcpServers"] and not force_write:
    print("skip")
    raise SystemExit(0)

root["mcpServers"][server_name] = server_definition
path.write_text(json.dumps(root, indent=2) + "\n", encoding="utf-8")
print("write")
PY
}

merge_claude_settings() {
  local target_settings_path="$1"
  local source_settings_path="$2"
  local force_write="$3"

  backup_file "$target_settings_path"
  ensure_directory "$(dirname "$target_settings_path")"

  python3 - "$target_settings_path" "$source_settings_path" "$force_write" <<'PY'
import json
import pathlib
import re
import sys


def sanitize(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"(?m)^\s*//.*$", "", text)
    text = re.sub(r",(\s*[}\]])", r"\1", text)
    return text


def read_json(path: pathlib.Path):
    if not path.exists() or not path.read_text(encoding="utf-8").strip():
        return {}
    raw = path.read_text(encoding="utf-8")
    try:
        return json.loads(raw)
    except Exception:
        return json.loads(sanitize(raw))


target_path = pathlib.Path(sys.argv[1])
source_path = pathlib.Path(sys.argv[2])
force_write = sys.argv[3] == "1"
source_root = read_json(source_path)
target_root = read_json(target_path)
if not isinstance(source_root, dict) or not isinstance(source_root.get("hooks"), dict):
    raise SystemExit(f"Source Claude settings do not contain a hooks object: {source_path}")
if not isinstance(target_root, dict):
    target_root = {}
if "$schema" not in target_root and "$schema" in source_root:
    target_root["$schema"] = source_root["$schema"]
if force_write or not isinstance(target_root.get("hooks"), dict):
    target_root["hooks"] = {}

for event_name, source_groups in source_root["hooks"].items():
    existing_groups = target_root["hooks"].get(event_name, [])
    if not isinstance(existing_groups, list):
        existing_groups = []

    preserved = []
    for group in existing_groups:
        is_nebula_group = False
        if isinstance(group, dict):
            for hook in group.get("hooks", []):
                if isinstance(hook, dict) and "command" in hook and "Invoke-NebulaAgentHook" in str(hook["command"]):
                    is_nebula_group = True
                    break
        if not is_nebula_group:
            preserved.append(group)

    target_root["hooks"][event_name] = preserved + list(source_groups)

target_path.write_text(json.dumps(target_root, indent=2) + "\n", encoding="utf-8")
PY
}

ensure_env_template() {
  local configured_env_path="$1"
  local template_root="$2"
  local raw_base_url="$3"
  local source_env_path
  source_env_path="$(resolve_template_file "$template_root" ".env" "$raw_base_url" 0 || true)"
  if [[ -z "$source_env_path" ]]; then
    source_env_path="$(resolve_template_file "$template_root" ".env.example" "$raw_base_url" 0 || true)"
  fi

  if [[ -z "$source_env_path" ]]; then
    printf 'Skip env template: no source file found (.env or .env.example).\n'
    return
  fi

  if [[ -f "$configured_env_path" && "$FORCE" -ne 1 ]]; then
    printf 'Skip existing env file: %s\n' "$configured_env_path"
    return
  fi

  ensure_directory "$(dirname "$configured_env_path")"
  cp "$source_env_path" "$configured_env_path"
  printf 'Wrote env file from %s to: %s\n' "$source_env_path" "$configured_env_path"
}

setup_copilot_user() {
  local configured_server_name="$1"
  local server_definition_json="$2"
  local config_path
  if [[ -n "$COPILOT_CONFIG_PATH" ]]; then
    config_path="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$COPILOT_CONFIG_PATH")"
  else
    config_path="$(get_default_copilot_config_path)"
  fi

  local action
  action="$(upsert_mcp_server "$config_path" "$configured_server_name" "$server_definition_json" "$FORCE")"
  if [[ "$action" == "skip" ]]; then
    printf "Skip existing '%s' in mcpServers (use --force to overwrite).\n" "$configured_server_name"
  else
    printf 'Wrote config: %s\n' "$config_path"
  fi

  printf '\nCopilot user-level MCP setup complete.\n'
}

setup_claude_user() {
  local configured_server_name="$1"
  local server_definition_json="$2"
  local config_path
  if [[ -n "$CLAUDE_USER_CONFIG_PATH" ]]; then
    config_path="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$CLAUDE_USER_CONFIG_PATH")"
  else
    config_path="$(get_default_claude_user_config_path)"
  fi

  local action
  action="$(upsert_mcp_server "$config_path" "$configured_server_name" "$server_definition_json" "$FORCE")"
  if [[ "$action" == "skip" ]]; then
    printf "Skip existing '%s' in mcpServers (use --force to overwrite).\n" "$configured_server_name"
  else
    printf 'Wrote config: %s\n' "$config_path"
  fi

  printf '\nClaude user-level MCP setup complete.\n'
}

setup_claude_project_mcp() {
  local project_path="$1"
  local configured_server_name="$2"
  local server_definition_json="$3"
  local project_root config_path
  project_root="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$project_path")"
  if [[ -n "$CLAUDE_PROJECT_CONFIG_PATH" ]]; then
    config_path="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$CLAUDE_PROJECT_CONFIG_PATH")"
  else
    config_path="$project_root/.mcp.json"
  fi

  local action
  action="$(upsert_mcp_server "$config_path" "$configured_server_name" "$server_definition_json" "$FORCE")"
  if [[ "$action" == "skip" ]]; then
    printf "Skip existing '%s' in mcpServers (use --force to overwrite).\n" "$configured_server_name"
  else
    printf 'Wrote config: %s\n' "$config_path"
  fi

  printf '\nClaude project-level MCP setup complete: %s\n' "$config_path"
}

setup_project() {
  local project_path="$1"
  shift
  local resolved_client_targets=("$@")
  local target_root
  target_root="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$project_path")"
  local script_directory
  script_directory="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$(dirname "$0")")"
  if [[ "$target_root" == "$script_directory" ]]; then
    echo "Refusing to scaffold into the scripts directory. Use --target-path to point at your project root." >&2
    exit 1
  fi

  local shared_templates=(
    'AGENTS.md'
    '.github/nebula.instructions.md'
    '.github/instructions/rag.instructions.md'
    '.github/instructions/coding.instructions.md'
    '.github/instructions/documentation.instructions.md'
    '.github/nebula/hooks/Invoke-NebulaAgentHook.ps1'
    '.github/nebula/hooks/Invoke-NebulaAgentHook.sh'
  )

  local relative_path source_path destination_path
  for relative_path in "${shared_templates[@]}"; do
    source_path="$(resolve_template_file "$TEMPLATE_ROOT" "$relative_path" "$TEMPLATE_RAW_BASE_URL" 1)"
    destination_path="$target_root/$relative_path"
    copy_file_safe "$source_path" "$destination_path" "$FORCE"
  done

  if array_contains "Copilot" "${resolved_client_targets[@]}"; then
    source_path="$(resolve_template_file "$TEMPLATE_ROOT" '.github/copilot-instructions.md' "$TEMPLATE_RAW_BASE_URL" 1)"
    copy_file_safe "$source_path" "$target_root/.github/copilot-instructions.md" "$FORCE"

    source_path="$(resolve_template_file "$TEMPLATE_ROOT" '.github/hooks/nebula-balanced.json' "$TEMPLATE_RAW_BASE_URL" 1)"
    copy_file_safe "$source_path" "$target_root/.github/hooks/nebula-balanced.json" "$FORCE"
  fi

  if array_contains "ClaudeCode" "${resolved_client_targets[@]}"; then
    source_path="$(resolve_template_file "$TEMPLATE_ROOT" '.claude/settings.bash.json' "$TEMPLATE_RAW_BASE_URL" 1)"
    local target_settings_path
    if [[ -n "$CLAUDE_SETTINGS_PATH" ]]; then
      target_settings_path="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$CLAUDE_SETTINGS_PATH")"
    else
      target_settings_path="$target_root/.claude/settings.json"
    fi
    merge_claude_settings "$target_settings_path" "$source_path" "$FORCE"
    printf 'Wrote config: %s\n' "$target_settings_path"
  fi

  if [[ "$SKIP_SKILL" -ne 1 ]]; then
    source_path="$(resolve_template_file "$TEMPLATE_ROOT" '.github/skills/nebularag/SKILL.md' "$TEMPLATE_RAW_BASE_URL" 1)"
    copy_file_safe "$source_path" "$target_root/.github/skills/nebularag/SKILL.md" "$FORCE"
  fi

  source_path="$(resolve_template_file "$TEMPLATE_ROOT" '.env.example' "$TEMPLATE_RAW_BASE_URL" 0 || true)"
  if [[ -n "$source_path" ]]; then
    copy_file_safe "$source_path" "$target_root/.env.example" "$FORCE"
  fi

  ensure_gitignore_entry "$target_root/.gitignore" '.env'
  ensure_gitignore_entry "$target_root/.gitignore" '.claude/settings.local.json'

  printf '\nProject setup complete: %s\n' "$target_root"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --mode)
      MODE="${2:-}"
      shift 2
      ;;
    --target-path)
      TARGET_PATH="${2:-}"
      shift 2
      ;;
    --client-targets)
      CLIENT_TARGETS="${2:-}"
      shift 2
      ;;
    --copilot-config-path|--user-config-path)
      COPILOT_CONFIG_PATH="${2:-}"
      shift 2
      ;;
    --claude-user-config-path|--claude-user-config)
      CLAUDE_USER_CONFIG_PATH="${2:-}"
      shift 2
      ;;
    --claude-project-config-path)
      CLAUDE_PROJECT_CONFIG_PATH="${2:-}"
      shift 2
      ;;
    --claude-settings-path)
      CLAUDE_SETTINGS_PATH="${2:-}"
      shift 2
      ;;
    --channel)
      CHANNEL="${2:-}"
      shift 2
      ;;
    --server-name)
      SERVER_NAME="${2:-}"
      shift 2
      ;;
    --image-name)
      IMAGE_NAME="${2:-}"
      shift 2
      ;;
    --template-raw-base-url)
      TEMPLATE_RAW_BASE_URL="${2:-}"
      shift 2
      ;;
    --home-assistant-mcp-url)
      HOMEASSISTANT_MCP_URL="${2:-}"
      shift 2
      ;;
    --external-home-assistant-mcp-url)
      EXTERNAL_HOMEASSISTANT_MCP_URL="${2:-}"
      shift 2
      ;;
    --use-external-home-assistant-url)
      USE_EXTERNAL_HOMEASSISTANT_URL=1
      shift
      ;;
    --force-external)
      FORCE_EXTERNAL=1
      shift
      ;;
    --install-target)
      INSTALL_TARGET="${2:-}"
      shift 2
      ;;
    --env-file-path)
      ENV_FILE_PATH="${2:-}"
      shift 2
      ;;
    --create-env-template)
      CREATE_ENV_TEMPLATE=1
      shift
      ;;
    --skip-skill)
      SKIP_SKILL=1
      shift
      ;;
    --no-backup)
      NO_BACKUP=1
      shift
      ;;
    --force)
      FORCE=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

require_python
validate_choice "$MODE" Both User Project
validate_choice "$CHANNEL" Auto Code Insiders Both
validate_choice "$INSTALL_TARGET" Ask LocalContainer HomeAssistantAddon

TEMPLATE_ROOT="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$(dirname "$0")/..")"
RESOLVED_INSTALL_TARGET="$(resolve_install_target)"
RESOLVED_HOMEASSISTANT_MCP_URL="$(resolve_homeassistant_mcp_url)"
resolve_client_targets "$CLIENT_TARGETS" RESOLVED_CLIENT_TARGETS

if [[ "$RESOLVED_INSTALL_TARGET" == "LocalContainer" ]] && ! command_exists podman; then
  warn "podman was not found in PATH. LocalContainer registrations will still be written, but they will not run until podman is installed."
fi

if [[ "$MODE" == "Both" || "$MODE" == "User" ]]; then
  if array_contains "Copilot" "${RESOLVED_CLIENT_TARGETS[@]}"; then
    server_definition_json="$(build_server_definition "$RESOLVED_INSTALL_TARGET" "$RESOLVED_HOMEASSISTANT_MCP_URL" "$IMAGE_NAME" "$ENV_FILE_PATH" '')"
    setup_copilot_user "$SERVER_NAME" "$server_definition_json"
  fi

  if array_contains "ClaudeCode" "${RESOLVED_CLIENT_TARGETS[@]}"; then
    server_definition_json="$(build_server_definition "$RESOLVED_INSTALL_TARGET" "$RESOLVED_HOMEASSISTANT_MCP_URL" "$IMAGE_NAME" "$ENV_FILE_PATH" '')"
    setup_claude_user "$SERVER_NAME" "$server_definition_json"
  fi

  if [[ "$CREATE_ENV_TEMPLATE" -eq 1 && "$RESOLVED_INSTALL_TARGET" == "LocalContainer" ]]; then
    ensure_env_template "$ENV_FILE_PATH" "$TEMPLATE_ROOT" "$TEMPLATE_RAW_BASE_URL"
  fi
fi

if [[ "$MODE" == "Both" || "$MODE" == "Project" ]]; then
  if [[ -z "$TARGET_PATH" ]]; then
    if [[ "$MODE" == "Both" ]]; then
      TARGET_PATH="$TEMPLATE_ROOT"
    else
      echo "--target-path is required when --mode Project." >&2
      exit 1
    fi
  fi

  setup_project "$TARGET_PATH" "${RESOLVED_CLIENT_TARGETS[@]}"

  if array_contains "ClaudeCode" "${RESOLVED_CLIENT_TARGETS[@]}"; then
    server_definition_json="$(build_server_definition "$RESOLVED_INSTALL_TARGET" "$RESOLVED_HOMEASSISTANT_MCP_URL" "$IMAGE_NAME" "$ENV_FILE_PATH" "\${PWD}")"
    setup_claude_project_mcp "$TARGET_PATH" "$SERVER_NAME" "$server_definition_json"
  fi
fi

printf '\nNebulaRAG setup finished.\n'
printf 'Clients: %s\n' "${RESOLVED_CLIENT_TARGETS[*]}"
printf 'Install target: %s\n' "$RESOLVED_INSTALL_TARGET"
printf 'Server: %s\n' "$SERVER_NAME"
if [[ "$RESOLVED_INSTALL_TARGET" == "HomeAssistantAddon" ]]; then
  printf 'MCP URL: %s\n' "$RESOLVED_HOMEASSISTANT_MCP_URL"
else
  printf 'Image: %s\n' "$IMAGE_NAME"
  printf 'Env file: %s\n' "$ENV_FILE_PATH"
  printf 'Note: Copilot CLI MCP registration is user-level. Project-local hooks are scaffolded into .github/hooks and Claude project MCP config is written to .mcp.json.\n'
fi
