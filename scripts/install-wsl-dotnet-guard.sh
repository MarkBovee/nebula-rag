#!/usr/bin/env bash
set -euo pipefail

wrapper_begin="# >>> nebula-wsl-dotnet-guard >>>"
wrapper_end="# <<< nebula-wsl-dotnet-guard <<<"
path_block_begin="# >>> nebula-local-bin-path >>>"
path_block_end="# <<< nebula-local-bin-path <<<"
force=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --force)
      force=1
      shift
      ;;
    -h|--help)
      cat <<'USAGE'
Usage: install-wsl-dotnet-guard.sh [--force]

Installs a WSL-side dotnet wrapper that blocks build-like commands from repos
located under /mnt/* and points you at a Linux-side workspace instead.

Options:
  --force    Overwrite an existing ~/.local/bin/dotnet wrapper after backing it up
  -h, --help Show help
USAGE
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 1
      ;;
  esac
done

if ! grep -qi microsoft /proc/version 2>/dev/null; then
  echo "This installer is intended for WSL environments only." >&2
  exit 1
fi

home_dir="${HOME:-}"
if [[ -z "$home_dir" ]]; then
  echo "HOME is not set." >&2
  exit 1
fi

target_dir="$home_dir/.local/bin"
target_path="$target_dir/dotnet"
backup_path="$target_dir/dotnet.pre-nebula-wsl-guard.bak"
real_dotnet="$(command -v dotnet || true)"

if [[ -z "$real_dotnet" ]]; then
  echo "dotnet is not installed in this WSL environment." >&2
  exit 1
fi

if [[ "$real_dotnet" == "$target_path" ]]; then
  real_dotnet="/usr/bin/dotnet"
fi

if [[ ! -x "$real_dotnet" ]]; then
  echo "Resolved dotnet executable is not runnable: $real_dotnet" >&2
  exit 1
fi

mkdir -p "$target_dir"

if [[ -f "$target_path" ]] && ! grep -Fq "$wrapper_begin" "$target_path"; then
  if [[ "$force" -ne 1 ]]; then
    echo "Refusing to overwrite existing $target_path." >&2
    echo "Re-run with --force to back it up to $backup_path first." >&2
    exit 1
  fi

  cp "$target_path" "$backup_path"
  echo "Backed up existing wrapper to $backup_path"
fi

python3 - "$target_path" "$real_dotnet" <<'PY'
from pathlib import Path
import sys

target_path = Path(sys.argv[1])
real_dotnet = sys.argv[2]
content = f"""#!/usr/bin/env bash
set -euo pipefail
# >>> nebula-wsl-dotnet-guard >>>
real_dotnet='{real_dotnet}'
command_name="${{1:-}}"
current_dir="$(pwd -P)"

case "$command_name" in
  build|test|run|publish|pack|clean|restore|msbuild)
    if [[ "${{NEBULA_ALLOW_MNT_DOTNET:-0}}" != "1" && "${{WSL_DOTNET_GUARD_DISABLE:-0}}" != "1" && "$current_dir" == /mnt/* ]]; then
      printf "✗ Nebula WSL dotnet guard blocked this build-like command.\\n" >&2
      printf "Current directory: %s\\n\\n" "$current_dir" >&2
      cat >&2 <<'MSG'
Mounted Windows drives under /mnt/* are leaving MSBuildTemp* and other transient
directories in repo roots. Build from a Linux-side workspace instead.

Suggested workflow:
  mkdir -p ~/projects
  git clone <remote> ~/projects/<repo>

Then open the Linux-side path from Windows via:
  \\\\wsl$\\<distro>\\home\\<user>\\projects\\<repo>

Temporary bypass for one command:
  NEBULA_ALLOW_MNT_DOTNET=1 dotnet build ...
MSG
      exit 64
    fi
    ;;
esac

exec "$real_dotnet" "$@"
# <<< nebula-wsl-dotnet-guard <<<
"""
target_path.write_text(content, encoding="utf-8", newline="\n")
PY

chmod 755 "$target_path"

ensure_path_file() {
  local profile_path="$1"

  if [[ -f "$profile_path" ]] && grep -Fq "$path_block_begin" "$profile_path"; then
    return
  fi

  cat >>"$profile_path" <<'EOF'

# >>> nebula-local-bin-path >>>
if [ -d "\$HOME/.local/bin" ] && [[ ":\$PATH:" != *":\$HOME/.local/bin:"* ]]; then
  export PATH="\$HOME/.local/bin:\$PATH"
fi
# <<< nebula-local-bin-path <<<
EOF
}

ensure_path_file "$home_dir/.profile"
ensure_path_file "$home_dir/.bashrc"
if [[ -f "$home_dir/.zshrc" ]]; then
  ensure_path_file "$home_dir/.zshrc"
fi

echo "Installed WSL dotnet guard to $target_path"
echo "Open a new WSL shell, or run: export PATH=\"\$HOME/.local/bin:\$PATH\""
