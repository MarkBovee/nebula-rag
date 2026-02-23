# Changelog

All notable changes to the Nebula RAG Home Assistant add-on are documented in this file.

The format is inspired by Keep a Changelog and follows semantic versioning.

## [0.1.2] - 2026-02-23

- Added add-on runtime dependency `libgssapi-krb5-2` to avoid `libgssapi_krb5.so.2` load warnings.
- Public repository hardening update: added security policy, dependency automation, and security CI workflow.
- Documentation refresh for clearer setup, operations, troubleshooting, and release process.

## [0.1.1] - 2026-02-23

- Add-on install readiness update.
- Added `DOCS.md` for Home Assistant add-on documentation.
- Added add-on metadata (`url`, `stage`) in `config.json`.
- Restricted supported architectures to `amd64` and `aarch64`.

## [0.1.0] - 2026-02-23

- Initial Home Assistant add-on release.
- Added add-on manifest (`config.json`) with configurable PostgreSQL connection settings.
- Added one-shot operations: `init`, `index`, `query`, `stats`, `list-sources`, and `health-check`.
- Added add-on runtime script (`run.sh`) that maps Home Assistant options to `NEBULARAG_*` runtime settings.
- Added add-on build configuration (`build.yaml`) and container build (`Dockerfile`).
- Added version bump helper script (`scripts/bump-ha-addon-version.ps1`).
