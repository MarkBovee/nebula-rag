#!/usr/bin/env bash
set -euo pipefail

agent=""
event=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --agent)
      agent="${2:-}"
      shift 2
      ;;
    --event)
      event="${2:-}"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [[ -z "$agent" || -z "$event" ]]; then
  echo "Usage: $0 --agent <Claude|Copilot> --event <SessionStart|PreToolUse|PostToolUse|PostToolUseFailure|ErrorOccurred|StopFailure>" >&2
  exit 2
fi

if ! command -v python3 >/dev/null 2>&1; then
  echo "python3 is required for $0" >&2
  exit 1
fi

export NEBULA_AGENT="$agent"
export NEBULA_EVENT="$event"
payload_file="$(mktemp)"
trap 'rm -f "$payload_file"' EXIT
cat >"$payload_file"
export NEBULA_HOOK_PAYLOAD_FILE="$payload_file"

python3 - <<'PY'
import datetime as _dt
import json
import os
import pathlib
import re
import sys
import tempfile
import urllib.error
import urllib.request

agent = os.environ["NEBULA_AGENT"]
event = os.environ["NEBULA_EVENT"]
raw = pathlib.Path(os.environ["NEBULA_HOOK_PAYLOAD_FILE"]).read_text(encoding="utf-8")
try:
    payload = json.loads(raw) if raw.strip() else {}
except Exception:
    payload = {"raw": raw}


def get_project_root() -> pathlib.Path:
    cwd = payload.get("cwd")
    if isinstance(cwd, str) and cwd.strip():
        return pathlib.Path(cwd).expanduser().resolve()
    return pathlib.Path.cwd()


def get_hook_home() -> pathlib.Path:
    home = pathlib.Path(os.path.expanduser("~")) if os.path.expanduser("~") else pathlib.Path(tempfile.gettempdir())
    hook_home = home / ".nebula-rag" / "hooks"
    hook_home.mkdir(parents=True, exist_ok=True)
    return hook_home


def write_log(category: str, **extra: object) -> None:
    entry = {
        "timestampUtc": _dt.datetime.now(tz=_dt.timezone.utc).isoformat().replace("+00:00", "Z"),
        "agent": agent,
        "event": event,
        "cwd": str(get_project_root()),
    }
    entry.update(extra)
    log_path = get_hook_home() / f"{category}.jsonl"
    with log_path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(entry, separators=(",", ":")) + "\n")


def get_nebula_server_definition(project_root: pathlib.Path):
    config_path = project_root / ".mcp.json"
    if not config_path.exists():
        return None

    try:
        config = json.loads(config_path.read_text(encoding="utf-8"))
    except Exception:
        return None

    servers = config.get("mcpServers")
    if not isinstance(servers, dict):
        return None

    server = servers.get("nebula-rag")
    return server if isinstance(server, dict) else None


def test_nebula_http_endpoint(url: str) -> str:
    request = urllib.request.Request(
        url,
        data=json.dumps({"jsonrpc": "2.0", "id": "hook-health", "method": "ping"}).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )

    try:
        with urllib.request.urlopen(request, timeout=3):
            return "healthy"
    except Exception:
        return "unreachable"


def get_tool_name():
    return payload.get("tool_name") if agent == "Claude" else payload.get("toolName")


def get_copilot_tool_args() -> dict:
    tool_args = payload.get("toolArgs")
    if isinstance(tool_args, dict):
        return tool_args
    if isinstance(tool_args, str) and tool_args.strip():
        try:
            parsed = json.loads(tool_args)
            return parsed if isinstance(parsed, dict) else {"raw": tool_args}
        except Exception:
            return {"raw": tool_args}
    return {}


def get_command_text():
    if agent == "Claude":
        tool_input = payload.get("tool_input")
        if isinstance(tool_input, dict):
            command = tool_input.get("command")
            return command if isinstance(command, str) else None
        return None

    tool_args = get_copilot_tool_args()
    command = tool_args.get("command")
    return command if isinstance(command, str) else None


def write_decision(behavior: str, reason: str) -> None:
    if agent == "Claude":
        response = {
            "hookSpecificOutput": {
                "hookEventName": "PreToolUse",
                "permissionDecision": behavior,
                "permissionDecisionReason": reason,
            }
        }
    else:
        response = {
            "permissionDecision": behavior,
            "permissionDecisionReason": reason,
        }

    sys.stdout.write(json.dumps(response, separators=(",", ":")))


def invoke_session_start() -> None:
    project_root = get_project_root()
    server_definition = get_nebula_server_definition(project_root)
    transport = "not-configured"
    health = "unknown"

    if isinstance(server_definition, dict):
        transport = str(server_definition.get("type") or "unknown")
        if transport == "http" and isinstance(server_definition.get("url"), str):
            health = test_nebula_http_endpoint(server_definition["url"])

    write_log("agent-events", phase="session-start", transport=transport, health=health)

    if agent == "Claude":
        message = "NebulaRAG balanced hooks active. "
        if transport == "http":
            message += f"Project MCP transport: http ({health}). "
        elif transport == "stdio":
            message += "Project MCP transport: stdio. "
        else:
            message += "Nebula MCP config not detected in .mcp.json. "

        message += "For project-specific work, prefer Nebula memory recall for prior decisions and rag_query for current source context. If Nebula fails, capture the issue and fall back to direct source inspection."
        sys.stdout.write(message)


def invoke_pre_tool_use() -> None:
    tool_name = get_tool_name()
    command_text = get_command_text()
    if not command_text:
        return

    blocked_rules = [
        (r"(?i)\brm\s+-rf\s+/(?:\s|$)", "Refusing destructive root delete command."),
        (r"(?i)\bgit\s+reset\s+--hard\b", "Refusing destructive git hard reset command."),
        (r"(?i)\bgit\s+checkout\s+--\s", "Refusing destructive git checkout restore command."),
        (r"(?i)\bmkfs(?:\.\w+)?\b", "Refusing filesystem formatting command."),
        (r":\(\)\s*\{\s*:\|:\&\s*;\s*\}\s*;", "Refusing fork bomb command."),
        (r"(?i)\b(?:shutdown|reboot|poweroff)\b", "Refusing system power command."),
    ]

    for pattern, reason in blocked_rules:
        if re.search(pattern, command_text):
            write_log("policy-events", tool=tool_name, command=command_text, decision="deny", reason=reason)
            write_decision("deny", reason)
            return


def invoke_post_tool() -> None:
    tool_name = get_tool_name()
    command_text = get_command_text()
    result_type = None
    result_text = None

    if agent == "Copilot":
        tool_result = payload.get("toolResult")
        if isinstance(tool_result, dict):
            result_type = tool_result.get("resultType")
            result_text = tool_result.get("textResultForLlm")

    looks_like_nebula_issue = bool(re.search(r"nebula", str(tool_name or ""), re.IGNORECASE) or re.search(r"nebula", str(command_text or ""), re.IGNORECASE))
    write_log(
        "tool-events",
        tool=tool_name,
        command=command_text,
        resultType=result_type,
        resultText=result_text,
        nebulaRelated=looks_like_nebula_issue,
    )


def invoke_error_hook() -> None:
    error_payload = payload.get("error")
    error_name = None
    error_message = None
    if isinstance(error_payload, dict):
        error_name = error_payload.get("name")
        error_message = error_payload.get("message")
    write_log("errors", errorName=error_name, errorMessage=error_message)


if event == "SessionStart":
    invoke_session_start()
elif event == "PreToolUse":
    invoke_pre_tool_use()
elif event in {"PostToolUse", "PostToolUseFailure"}:
    invoke_post_tool()
elif event in {"ErrorOccurred", "StopFailure"}:
    invoke_error_hook()
PY
