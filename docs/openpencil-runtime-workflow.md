# OpenPencil Runtime Workflow

This document explains how OpenPencil is wired into NebulaRAG after the switch to an upstream-owned runtime plus a private in-repo mirror.

Use this guide when you need to:

- start the OpenPencil editor and MCP runtime for Nebula work,
- understand why there is both a submodule and a standalone working clone,
- update the private OpenPencil mirror,
- refresh the Nebula submodule to a newer OpenPencil commit,
- or troubleshoot the live-loop design flow.

## Overview

Nebula now uses OpenPencil in three separate roles:

1. The upstream OpenPencil codebase remains the real product/runtime source.
2. A private GitHub mirror under `MarkBovee/open-pencil` is used so Nebula can depend on a stable private copy.
3. Nebula vendors that private mirror as a git submodule at `open-pencil/`.

That means there are two valid local OpenPencil locations with different jobs:

- `open-pencil/` inside this repo: the vendored submodule used by Nebula.
- `e:\Projects\Personal\open-pencil`: the standalone working clone used to make and push OpenPencil changes.

The standalone clone should usually stay out of the active VS Code workspace once the submodule exists, otherwise you see two OpenPencil roots.

## Current Contract

The Nebula integration assumes these OpenPencil endpoints:

- Editor: `http://localhost:1420`
- MCP: `http://localhost:3100/mcp`
- File route: `http://localhost:3100/file?path=designs/<name>.fig`

Nebula reads these optional settings from `.env`:

```text
OPENPENCIL_EDITOR_URL=http://localhost:1420
OPENPENCIL_MCP_URL=http://localhost:3100/mcp
```

If they are not set, the Nebula scripts fall back to the default local URLs above.

## Repository Layout

### In Nebula

- `open-pencil/` is the private submodule clone.
- `designs/*.fig` contains the actual design artifacts for Nebula.
- `.github/skills/openpencil-design/scripts/start-openpencil-live-loop.ps1` watches the design files and reopens the editor on the latest validated artifact.

### In the standalone OpenPencil clone

- `e:\Projects\Personal\open-pencil` is the place where OpenPencil changes should be developed and committed.
- Local branch tracking should stay on `origin/master`.
- The `markbovee` remote exists only as the private mirror push target.

## Why Both a Submodule and a Standalone Clone Exist

The submodule solves portability for Nebula clones.

Without it, cloning Nebula elsewhere would not bring along the OpenPencil source tree needed to build the container runtime locally.

The standalone clone solves day-to-day OpenPencil development.

Using a separate working clone is simpler for:

- updating against upstream `open-pencil/open-pencil`,
- keeping local branch tracking pointed at upstream,
- and pushing selected commits to the private `markbovee` mirror.

In short:

- develop OpenPencil in the standalone clone,
- publish that commit to the private mirror,
- then update the Nebula submodule to that mirrored commit.

## Starting the Runtime

From the vendored submodule inside Nebula:

```powershell
Set-Location .\open-pencil
pwsh .\start-openpencil.ps1 -UseContainer -WorkspacePath ..
```

That starts:

- the editor on `http://localhost:1420`,
- the MCP server on `http://localhost:3100/mcp`,
- and exposes the Nebula repo as the MCP file root.

The `-WorkspacePath ..` part matters. It makes the OpenPencil MCP file route serve Nebula's `designs/*.fig` files instead of files from the OpenPencil repo itself.

For local non-container mode:

```powershell
Set-Location .\open-pencil
pwsh .\start-openpencil.ps1 -WorkspacePath ..
```

## Live-Loop Flow

The Nebula live-loop script is:

```powershell
pwsh .\.github\skills\openpencil-design\scripts\start-openpencil-live-loop.ps1 -VariantsRoot designs -Watch
```

What it does:

1. Watches `designs/*.fig`.
2. Validates the latest `.fig` archive before use.
3. Builds an OpenPencil `open=` URL to the latest design.
4. Prefers the MCP file route when available.
5. Falls back to the old sibling-`public/` mirror approach only if needed.

This is why the OpenPencil runtime now needs both the editor URL and the MCP URL.

## Private Mirror Workflow

When OpenPencil changes need to be preserved privately:

1. Work in `e:\Projects\Personal\open-pencil`.
2. Keep `master` tracking `origin/master`.
3. Commit locally.
4. Push the desired commit to the private mirror remote:

```powershell
git push markbovee master
```

5. Update the Nebula submodule to the new mirrored commit.

The remote layout in the standalone clone should look like this:

- `origin` → upstream `https://github.com/open-pencil/open-pencil.git`
- `markbovee` → private mirror `https://github.com/MarkBovee/open-pencil.git`

The local branch upstream should remain:

```text
master -> origin/master
```

It should not track `markbovee/master` by default.

## Updating the Nebula Submodule

After a new OpenPencil commit is pushed to the private mirror:

```powershell
Set-Location e:\Projects\Personal\nebula-rag
git submodule update --init --remote open-pencil
```

If you want to pin the submodule to a specific mirrored commit instead of the latest remote head:

```powershell
Set-Location e:\Projects\Personal\nebula-rag\open-pencil
git fetch origin
git checkout <commit>
Set-Location ..
git add open-pencil
```

Then commit the submodule pointer in Nebula.

## What Belongs in OpenPencil Git

These belong in the private mirror and submodule:

- runtime code,
- container assets,
- startup scripts,
- MCP server changes,
- OpenPencil README and changelog updates.

These do not belong there:

- transient `public/*.fig` live-loop mirrors from Nebula work.

The standalone OpenPencil `.gitignore` now ignores `public/*.fig` for exactly that reason.

## What Belongs in Nebula Git

These belong in Nebula:

- `designs/*.fig`,
- Nebula OpenPencil helper scripts,
- `.mcp.json`,
- `.env.example` defaults,
- README and docs for the integration,
- the `open-pencil/` submodule pointer,
- `.gitmodules`.

## Updating This Setup Safely

When the integration changes, update these files together:

- `README.md` for the short public-facing overview.
- `docs/openpencil-runtime-workflow.md` for the operational details.
- `nebula-rag/config.json` for the add-on version bump.
- `nebula-rag/CHANGELOG.md` for the matching release note.

If the runtime contract changes, also check:

- `.env.example`
- `.mcp.json`
- `.github/skills/openpencil-design/scripts/openpencil-common.ps1`
- `.github/skills/openpencil-design/scripts/start-openpencil-live-loop.ps1`

## Troubleshooting

### Two OpenPencil projects appear in VS Code

Cause:

- both the standalone clone and the submodule are open as workspace roots.

Fix:

- keep the Nebula repo root open,
- remove the standalone `e:\Projects\Personal\open-pencil` workspace root,
- reload the VS Code window if Explorer still shows stale roots.

### Live-loop opens an empty canvas

Check:

1. OpenPencil is running.
2. The editor is on `http://localhost:1420`.
3. MCP is on `http://localhost:3100/mcp`.
4. The runtime was started with `-WorkspacePath ..` or another path that points to the Nebula repo root.
5. The current `.fig` file is a valid archive.

### The file route returns 404

Check:

1. The MCP server was started with `OPENPENCIL_MCP_ROOT` pointing at the Nebula repo root.
2. The requested path is relative to that root, for example `designs/nebula-server-dashboard.fig`.
3. The file actually exists under `designs/`.

### The submodule is on the wrong commit

Fix:

1. Enter `open-pencil/` inside Nebula.
2. Fetch the desired mirrored commit.
3. Check out the correct commit.
4. Return to the Nebula root.
5. Stage the submodule pointer and commit it.

## Recommended Routine

For normal Nebula design work:

1. Start OpenPencil from the vendored `open-pencil/` submodule.
2. Point it at the Nebula repo with `-WorkspacePath ..`.
3. Run the Nebula live-loop watcher.
4. Save real design artifacts into `designs/*.fig`.

For OpenPencil runtime development:

1. Switch to the standalone clone at `e:\Projects\Personal\open-pencil`.
2. Make and test the runtime changes there.
3. Push the result to `markbovee`.
4. Update the Nebula submodule to the new mirrored commit.