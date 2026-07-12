#!/usr/bin/env bash
#
# SRXPanel uninstaller
#   bash uninstall.sh
#
set -euo pipefail

APP_DIR="/var/www/srxpanel"
CONFIG_DIR="/etc/srxpanel"

C_RESET='\033[0m'; C_GREEN='\033[0;32m'; C_YELLOW='\033[1;33m'; C_BLUE='\033[0;34m'; C_RED='\033[0;31m'
info() { echo -e "${C_BLUE}==>${C_RESET} $*"; }
ok()   { echo -e "${C_GREEN}✓${C_RESET} $*"; }
warn() { echo -e "${C_YELLOW}!${C_RESET} $*"; }
die()  { echo -e "${C_RED}✗${C_RESET} $*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || die "Run as root (sudo)."

warn "This will remove the SRXPanel application and its services."
read -rp 'Type UNINSTALL to confirm: ' confirm
[ "$confirm" = "UNINSTALL" ] || die "Aborted."

read -rp "Remove panel data (SQLite DB, backups, vhosts)? [y/N] " rm_data
read -rp "Drop the MySQL database and user? [y/N] " rm_db

info "Stopping and removing systemd service…"
systemctl stop srxpanel 2>/dev/null || true
systemctl disable srxpanel 2>/dev/null || true
rm -f /etc/systemd/system/srxpanel.service
systemctl daemon-reload
ok "Service removed"

info "Removing Nginx config…"
rm -f /etc/nginx/sites-enabled/srxpanel.conf /etc/nginx/sites-available/srxpanel.conf
nginx -t 2>/dev/null && systemctl reload nginx 2>/dev/null || true
ok "Nginx config removed"

info "Removing Fail2ban jail…"
rm -f /etc/fail2ban/jail.d/srxpanel.conf
systemctl restart fail2ban 2>/dev/null || true

if [[ "${rm_db,,}" == "y" ]]; then
  # shellcheck disable=SC1091
  source "$CONFIG_DIR/db.conf" 2>/dev/null || true
  if [ -n "${DB_NAME:-}" ] && [ -n "${MYSQL_ROOT_PASSWORD:-}" ]; then
    info "Dropping MySQL database ${DB_NAME}…"
    mysql -uroot -p"${MYSQL_ROOT_PASSWORD}" <<SQL || warn "Could not drop database"
DROP DATABASE IF EXISTS \`${DB_NAME}\`;
DROP USER IF EXISTS '${DB_USER}'@'localhost';
FLUSH PRIVILEGES;
SQL
    ok "Database dropped"
  fi
fi

info "Removing application files…"
rm -rf "$APP_DIR/publish" "$APP_DIR/src"

if [[ "${rm_data,,}" == "y" ]]; then
  rm -rf "$APP_DIR" "$CONFIG_DIR" /var/log/srxpanel /var/backups/srxpanel /var/www/vhosts
  ok "All panel data removed"
else
  warn "Kept data in $APP_DIR, $CONFIG_DIR, /var/backups/srxpanel"
fi

# SSL certificates are intentionally left in place (managed by certbot).
warn "Let's Encrypt certificates were left intact. Remove with: certbot delete"

ok "SRXPanel uninstalled."
