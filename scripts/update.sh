#!/usr/bin/env bash
#
# SRXPanel updater
#   curl -fsSL https://get.srxpanel.com/update.sh | bash
#
set -euo pipefail

APP_DIR="/var/www/srxpanel"
SRC_DIR="$APP_DIR/src"
PUBLISH_DIR="$APP_DIR/publish"
BACKUP_DIR="/var/backups/srxpanel"

C_RESET='\033[0m'; C_GREEN='\033[0;32m'; C_YELLOW='\033[1;33m'; C_BLUE='\033[0;34m'; C_RED='\033[0;31m'
info() { echo -e "${C_BLUE}==>${C_RESET} $*"; }
ok()   { echo -e "${C_GREEN}✓${C_RESET} $*"; }
warn() { echo -e "${C_YELLOW}!${C_RESET} $*"; }
die()  { echo -e "${C_RED}✗${C_RESET} $*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || die "Run as root (sudo)."
[ -d "$SRC_DIR/.git" ] || die "No SRXPanel source checkout at ${SRC_DIR}. Is the panel installed?"

CURRENT="$(cat "$PUBLISH_DIR/version.txt" 2>/dev/null || echo unknown)"
info "Current version: ${CURRENT}"

# Back up the database before updating
info "Backing up database…"
mkdir -p "$BACKUP_DIR"
TS="$(date +%Y%m%d-%H%M%S)"
if [ -f "$APP_DIR/srxpanel.db" ]; then
  cp "$APP_DIR/srxpanel.db" "$BACKUP_DIR/srxpanel-${TS}.db"
  ok "SQLite backup: $BACKUP_DIR/srxpanel-${TS}.db"
else
  source /etc/srxpanel/db.conf 2>/dev/null || true
  if [ -n "${DB_NAME:-}" ]; then
    mysqldump -u"${DB_USER}" -p"${DB_PASSWORD}" "${DB_NAME}" > "$BACKUP_DIR/${DB_NAME}-${TS}.sql" || warn "mysqldump failed"
    ok "MySQL backup: $BACKUP_DIR/${DB_NAME}-${TS}.sql"
  fi
fi

info "Pulling latest source…"
git -C "$SRC_DIR" fetch --all --tags
git -C "$SRC_DIR" pull --ff-only

info "Publishing…"
dotnet publish "$SRC_DIR/SRXPanel.csproj" -c Release -o "$PUBLISH_DIR"
[ -f "$SRC_DIR/appsettings.Production.json" ] && cp "$SRC_DIR/appsettings.Production.json" "$PUBLISH_DIR/"

NEW="$(grep -oP '"Version"\s*:\s*"\K[^"]+' "$SRC_DIR/appsettings.json" | head -1 || echo unknown)"
echo "$NEW" > "$PUBLISH_DIR/version.txt"

chown -R www-data:www-data "$APP_DIR"

info "Restarting service (migrations apply automatically on startup)…"
systemctl restart srxpanel
sleep 3

if systemctl is-active --quiet srxpanel; then
  ok "SRXPanel updated: ${CURRENT} → ${NEW}"
else
  die "Service failed to start after update. Check: journalctl -u srxpanel -n 50"
fi

echo
info "Changelog:"
if [ -f "$SRC_DIR/CHANGELOG.md" ]; then
  sed -n '1,40p' "$SRC_DIR/CHANGELOG.md"
else
  git -C "$SRC_DIR" log --oneline -n 10
fi
