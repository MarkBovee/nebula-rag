---
name: release-readiness
description: Enforce pre-merge release gates by checking dependency freshness against latest stable versions, applying safe updates, validating build/tests, and ensuring add-on version/changelog updates before merging and pushing main.
license: Complete terms in LICENSE.txt
---

# Release Readiness Gate

## Overview

Use this skill before any merge/push to main. It enforces two mandatory outcomes:

1. Dependencies are checked against latest stable versions in the ecosystems used by the repo.
2. Add-on version and changelog are updated when code/doc behavior changed.

This skill is for execution workflows, not theory. Prefer concrete commands, explicit pass/fail checks, and clear blocking criteria.

---

## Trigger Conditions

Use this skill when the user asks to:

- merge to main
- push to main
- prepare/deploy a release
- verify release readiness
- check package versions or dependency freshness

Also use automatically after non-trivial implementation tasks before final merge.

---

## Phase 1: Detect Project Ecosystems

1. Detect dependency managers present in repository:
- .NET: `*.csproj`, `Directory.Packages.props`, `NuGet.config`
- Node: `package.json`, lock files
- Python: `requirements*.txt`, `pyproject.toml`, `poetry.lock`

2. Build a per-ecosystem check plan.

Decision branch:
- If an ecosystem is not present, skip it and report "not applicable".

---

## Phase 2: Check Latest Stable Dependencies

### .NET / NuGet

1. Run outdated checks:
- `dotnet list <solution-or-project> package --outdated`
- If central package management exists, include impacted projects.

2. Classify updates:
- Patch/minor updates: generally safe candidates.
- Major updates: require compatibility review and user confirmation before applying.
- Pre-release packages: do not auto-upgrade to pre-release unless user requested.

3. Apply approved updates and restore/build.

### Node

1. Check outdated packages:
- `npm outdated` or ecosystem equivalent.

2. Prefer latest stable updates.
3. Avoid pre-release channels unless explicitly requested.

### Python

1. Inspect current pins/constraints.
2. Compare with latest stable package releases.
3. Update constraints conservatively and run verification.

Blocking criteria:
- Any upgrade causes build/test failures.
- Required major upgrade has unresolved breaking changes.

---

## Phase 3: Versioning and Release Notes Gate

Before merge/push to main, enforce:

1. `nebula-rag/config.json` version bump (default patch bump).
2. Matching `nebula-rag/CHANGELOG.md` entry with date and concise bullets.
3. Docs updates when behavior changed (`README.md` and/or add-on docs).

Blocking criteria:
- Missing version bump.
- Missing matching changelog entry.
- Inconsistent docs for user-visible behavior changes.

---

## Phase 4: Validate and Merge

1. Run full validation:
- `dotnet build NebulaRAG.slnx`
- `dotnet test NebulaRAG.slnx`

2. Confirm clean working tree except intended files.
3. Commit with conventional message.
4. Merge to main (non-fast-forward preferred).
5. Push main.

Final output must include:
- What dependency checks ran and outcomes.
- Which packages were updated (or none).
- Confirmed version bump and changelog entry.
- Build/test summary.
- Final main commit SHA.

---

## Operational Rules

- Never skip dependency freshness checks when merge/push main is requested.
- Never push main without version/changelog gate passing.
- Never use destructive git commands unless user explicitly approves.
- If unexpected unrelated workspace changes appear, pause and ask user how to proceed.

---

## Example Prompts

- "Run release readiness checks and merge to main."
- "Before pushing main, verify we’re on latest stable packages and bump add-on version."
- "Prepare this branch for deployment with dependency freshness + version/changelog gates."
