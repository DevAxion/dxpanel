# Contributing to SRXPanel

Thanks for your interest in improving SRXPanel! This guide covers how to get a dev
environment running and how to submit changes.

## Development setup

Requirements: [.NET 9 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/srxpanel/srxpanel
cd srxpanel
dotnet restore
dotnet run
```

On non-Linux hosts the panel runs in **simulation mode** — service commands (Nginx,
MySQL, BIND, etc.) are recorded in the Command Log instead of being executed, so the
full UI works on Windows/macOS without a server. The SQLite database and demo data are
created automatically on first run.

Sign in with the seeded accounts (see the README).

## Project layout

```
Models/       EF Core entities
Data/         DbContext, migrations, seeder
Services/     Business logic (Integration, Billing, Portal, Reseller, Api)
Pages/        Razor Pages (grouped by area/role)
scripts/      install.sh / update.sh / uninstall.sh
docker/       Dockerfile, docker-compose.yml, .env.example
```

## Making changes

1. **Branch** off `main`: `git checkout -b feature/my-change`.
2. **Keep the build green** — `dotnet build` must pass with no new warnings.
3. **Database changes** require a migration:
   ```bash
   dotnet ef migrations add MyChange
   ```
   Migrations are applied automatically on startup; don't edit existing migrations.
4. **Match the existing style** — Razor Pages with the `srx-card` UI conventions,
   nullable reference types enabled, services behind interfaces registered in
   `Program.cs`.
5. **Respect simulation mode** — anything that touches the OS must go through
   `ICommandRunner` so it degrades safely on dev hosts.

## Pull requests

- Keep PRs focused and describe the change and its motivation.
- Update [CHANGELOG.md](CHANGELOG.md) under an *Unreleased* section.
- Ensure `dotnet build` and the CI workflow pass.

## Reporting issues

Please include your OS, panel version (from `/api/health`), reproduction steps and any
relevant output from `journalctl -u srxpanel`.

## License

By contributing, you agree that your contributions are licensed under the
[MIT License](LICENSE).
