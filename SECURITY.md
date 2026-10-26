# Security Policy

## Supported Versions

Security fixes are provided for the latest stable release line for the core OpenMcdf library.

The OpenMcdf.Ole project is experimental and security is best-effort only.
Contributions are from the community are welcome.

| Project | Version | Supported |
| ------- | ------- | --------- |
| OpenMcdf | 3.x | :white_check_mark: |
| OpenMcdf | < 3.x | :x: |
| OpenMcdf.Ole | Any | :x: |

If you are using an older release, upgrade to the latest stable package before reporting a security issue.

## Reporting a Vulnerability

Please report suspected vulnerabilities privately.

1. Use GitHub Security Advisories (preferred):
   - <https://github.com/ironfede/openmcdf/security/advisories/new>
2. If advisory submission is not possible, open a private discussion with maintainers (<jeremy@visionaid.com>).

Please do not report security vulnerabilities in public issues.

When possible, include:

- A clear description of the issue and affected component(s)
- Reproduction steps or a minimal proof of concept
- Impact assessment (confidentiality, integrity, availability)
- A suggested fix or mitigation, if known
- Your preferred contact details for follow-up

## Response Process

- Triage acknowledgment target: within 7 days
- Status updates: provided as work progresses
- Fix and disclosure timeline: depends on severity and complexity
- Coordinated disclosure is preferred

After a fix is available, maintainers may publish a security advisory and release notes with relevant remediation details.

## Scope

This policy applies to:

- The core OpenMcdf library in this repository
- Official packages published from this repository

Third-party dependencies and downstream integrations may have separate security processes.
