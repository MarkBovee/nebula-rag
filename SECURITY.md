# Security Policy

## Supported Versions

| Version | Supported |
| --- | --- |
| main | Yes |
| < latest | No |

## Reporting a Vulnerability

Report security vulnerabilities privately by opening a GitHub Security Advisory draft for this repository:

- https://github.com/MarkBovee/NebulaRAG/security/advisories/new

If you cannot use GitHub Security Advisories, open a private issue with maintainers and do not disclose exploit details publicly.

When reporting, include:

- Affected component and version.
- Reproduction steps and proof of concept.
- Impact assessment and suggested remediation.

## Response Targets

- Initial triage response: within 3 business days.
- Confirmation and severity assessment: within 7 business days.
- Remediation timeline: shared after triage based on severity.

## Security Practices

- Secrets must not be committed to source control.
- Use `.nebula.env` for runtime credentials and keep it out of git.
- Keep dependencies updated and monitor automated security scans in GitHub Actions.
