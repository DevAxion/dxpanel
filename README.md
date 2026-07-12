<div align="center">

# SRXPanel

**Your own hosting control panel — an open-source cPanel alternative.**

[![Build](https://github.com/srxpanel/srxpanel/actions/workflows/build.yml/badge.svg)](https://github.com/srxpanel/srxpanel/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![Ubuntu](https://img.shields.io/badge/Ubuntu-22.04-E95420?logo=ubuntu&logoColor=white)](https://ubuntu.com/)

</div>

SRXPanel is a modern, self-hosted web hosting control panel built with ASP.NET Core 9.
Manage domains, email, MySQL databases, DNS, SSL, files, billing and resellers from a
single dark-themed dashboard — deployed to a fresh Ubuntu server with **one command**.

```bash
bash <(curl -fsSL https://raw.githubusercontent.com/SeniorSRX/srxpanel/main/scripts/install.sh)
```

## ✨ Features

| | |
|---|---|
| 🌐 **Domains** | Domains, subdomains, redirects & parked domains with automatic Nginx vhosts |
| 📧 **Email** | Mailboxes, forwarders & autoresponders (Postfix + Dovecot) |
| 🗄️ **Databases** | MySQL databases & users, phpMyAdmin |
| 🔒 **SSL** | One-click Let's Encrypt certificates with auto-renewal |
| 🧭 **DNS** | Authoritative DNS zones & records (BIND9) |
| 📁 **File Manager** | Browse, upload, edit & archive in the browser |
| 👥 **Resellers** | White-label panels with per-reseller limits & billing |
| 💳 **Billing** | Plans, subscriptions, invoices, coupons, affiliates (Stripe) |
| 🔌 **API** | REST API + WHMCS/Blesta provisioning modules |

## 🚀 Installation

### One-liner (Ubuntu 22.04)

```bash
bash <(curl -fsSL https://raw.githubusercontent.com/SeniorSRX/srxpanel/main/scripts/install.sh)
```

The interactive installer sets up Nginx, MySQL, PHP (7.4–8.3), the .NET 9 runtime,
Certbot, UFW and Fail2ban, deploys the panel as a `systemd` service and issues an SSL
certificate. Requirements: **Ubuntu 22.04**, 1 CPU, 1 GB RAM, 10 GB disk, root access.

### Docker

```bash
git clone https://github.com/srxpanel/srxpanel
cd srxpanel/docker
cp .env.example .env
nano .env            # set domain, passwords, Stripe keys
docker compose up -d
```

### From source (development)

```bash
git clone https://github.com/srxpanel/srxpanel
cd srxpanel
dotnet run
```

The app runs in **simulation mode** on non-Linux hosts — service commands are logged to
the Command Log instead of being executed, so you can develop the full UI on Windows/macOS.

Default seeded logins (dev):

| Role | Username | Password |
|------|----------|----------|
| SuperAdmin | `admin` | `Admin@123456!` |
| Reseller | `reseller1` | `Reseller@123456!` |
| Client | `client1` | `Client@123456!` |

## 🩺 Health check

```bash
curl -s https://panel.example.com/api/health | jq
```

```json
{
  "status": "healthy",
  "version": "1.0.0",
  "services": { "database": "ok", "nginx": "running", "mysql": "running", "email": "running", "dns": "running" },
  "uptime": "5d 3h 22m",
  "diskUsage": "45%",
  "memoryUsage": "62%"
}
```

## 🔄 Updating

From the server shell:

```bash
curl -fsSL https://get.srxpanel.com/update.sh | bash
```

…or in the panel under **Admin → Updates** (changelog, one-click update, history,
automatic updates toggle).

## 🧱 Tech stack

- **Backend:** ASP.NET Core 9, Razor Pages, EF Core
- **Database:** SQLite (default) / MySQL 8
- **UI:** Bootstrap 5 dark theme
- **Auth:** ASP.NET Identity (SuperAdmin / Reseller / Client) + JWT for the API
- **Payments:** Stripe

## 📚 Documentation

Full docs are served at `/docs` on any running instance, and the WHMCS/Blesta module
reference is under **API Docs** in the panel. See also [CHANGELOG.md](CHANGELOG.md) and
[CONTRIBUTING.md](CONTRIBUTING.md).

## 🤝 Contributing

Contributions are welcome — please read [CONTRIBUTING.md](CONTRIBUTING.md) first.

## 📄 License

Released under the [MIT License](LICENSE).
