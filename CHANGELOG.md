# Changelog

All notable changes to SRXPanel are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-07-08

First public release — production-ready.

### Added
- **Phase 7 — Production & distribution**
  - One-liner Ubuntu 22.04 installer (`scripts/install.sh`) with interactive prompts,
    package installation, MySQL provisioning, systemd service, Nginx reverse proxy,
    Let's Encrypt SSL, UFW firewall and Fail2ban hardening.
  - `scripts/update.sh` (in-place update with DB backup) and `scripts/uninstall.sh`.
  - Health check endpoint `GET /api/health` reporting status, version, service states,
    uptime and disk/memory usage.
  - In-panel version management at **Admin → Updates**: changelog, one-click update,
    update history and an automatic-updates toggle.
  - Docker support: `docker/Dockerfile`, `docker-compose.yml` (panel + MySQL + Nginx +
    Certbot) and `.env.example`.
  - Public marketing landing page (`/`) and documentation site (`/docs`).
  - GitHub project files: README, CHANGELOG, CONTRIBUTING, LICENSE, CI workflow.

### Previous phases
- **Phase 6B** — Reseller billing, multi-currency, affiliate system, WHMCS/Blesta &
  REST APIs.
- **Phase 6A** — Reseller system & white-label branding.
- **Phase 5** — Customer self-service portal (tickets, backups, API keys).
- **Phase 4** — Payments, provisioning & billing (Stripe).
- **Phase 3** — Linux service integration (Nginx, MySQL, DNS, FTP, email, SSL).
- **Phase 2** — Core hosting features (domains, databases, FTP, email, DNS, files).
- **Phase 1** — Authentication, roles, dashboard & user management.

[1.0.0]: https://github.com/srxpanel/srxpanel/releases/tag/v1.0.0
