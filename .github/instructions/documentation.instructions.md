---
applyTo: '**'
---

# Documentation Standards

- Use README for overview, docs for deep-dives, openspec for changes/specs
- Do not create temporary report/progress files

## Where Information Belongs

| Information Type | Location | NOT here |
|-----------------|----------|----------|
| Project overview | `README.md` | Scattered docs |
| History | Git commit messages | Summary files |
| Deep-dives (>100 lines) | `/docs/` | Root folder |

## No Temporary Files

**Never create:** `*_REPORT.md`, `*_SUMMARY.md`, `*_COMPLETE.md`, `*_GUIDE.md`, `PROGRESS_*.md`, `PHASE_*.md`, `IMPLEMENTATION_PLAN.md`

Git history and commit messages are the proper audit trail.

## README Updates

Update `README.md` when:
- Public-facing behavior changes
- Architecture or folder structure changes
- New configuration options are added
- Setup/deployment instructions change

## Documentation Tiers

### Tier 1: `README.md` (root)
Project overview, quick start, architecture, folder structure, configuration, deployment.

### Tier 2: `/docs/` (deep-dives)
Only for: complex integration guides (>100 lines), architecture deep-dives, operational runbooks, troubleshooting guides. Not for feature specs or implementation guides.

## When Code Changes

3. Update README if user-visible behavior changes
4. Update folder structure in README if files move
