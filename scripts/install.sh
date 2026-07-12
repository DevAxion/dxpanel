#!/usr/bin/env bash
#
# SRXPanel installer for Ubuntu 22.04 LTS
#   curl -fsSL https://get.srxpanel.com/install.sh | bash
#
set -euo pipefail

# Never let apt open interactive dialogs (e.g. Postfix config) during install.
export DEBIAN_FRONTEND=noninteractive

# ---------------------------------------------------------------------------
# Constants & helpers
# ---------------------------------------------------------------------------
SRX_VERSION="1.0.0"
APP_DIR="/var/www/srxpanel"
PUBLISH_DIR="$APP_DIR/publish"
CONFIG_DIR="/etc/srxpanel"
LOG_DIR="/var/log/srxpanel"
BACKUP_DIR="/var/backups/srxpanel"
VHOST_DIR="/var/www/vhosts"
REPO_URL="${SRX_REPO_URL:-https://github.com/srxpanel/srxpanel.git}"

C_RESET='\033[0m'; C_GREEN='\033[0;32m'; C_YELLOW='\033[1;33m'; C_RED='\033[0;31m'; C_BLUE='\033[0;34m'
info()  { echo -e "${C_BLUE}==>${C_RESET} $*"; }
ok()    { echo -e "${C_GREEN}✓${C_RESET} $*"; }
warn()  { echo -e "${C_YELLOW}!${C_RESET} $*"; }
err()   { echo -e "${C_RED}✗${C_RESET} $*" >&2; }
die()   { err "$*"; exit 1; }

gen_pw() { tr -dc 'A-Za-z0-9!@#%^_+=' </dev/urandom | head -c 24; }

# ---------------------------------------------------------------------------
# System checks
# ---------------------------------------------------------------------------
info "SRXPanel installer v${SRX_VERSION}"

[ "$(id -u)" -eq 0 ] || die "This installer must be run as root (use sudo)."

if [ -r /etc/os-release ]; then
  . /etc/os-release
  if [ "${ID:-}" != "ubuntu" ] || [ "${VERSION_ID:-}" != "22.04" ]; then
    die "Unsupported OS: ${PRETTY_NAME:-unknown}. SRXPanel requires Ubuntu 22.04 LTS."
  fi
else
  die "Cannot detect the operating system (/etc/os-release missing)."
fi
ok "Ubuntu 22.04 detected"

CPU_CORES=$(nproc)
RAM_MB=$(free -m | awk '/^Mem:/{print $2}')
DISK_GB=$(df -BG --output=avail / | tail -1 | tr -dc '0-9')
[ "$CPU_CORES" -ge 1 ]   || die "At least 1 CPU core is required."
[ "$RAM_MB" -ge 900 ]    || die "At least 1 GB of RAM is required (found ${RAM_MB} MB)."
[ "$DISK_GB" -ge 10 ]    || die "At least 10 GB of free disk is required (found ${DISK_GB} GB)."
ok "Resources OK: ${CPU_CORES} CPU, ${RAM_MB} MB RAM, ${DISK_GB} GB free disk"

# When piped (curl ... | bash), stdin is the pipe, so every interactive `read`
# hits EOF immediately and prompt loops never terminate. Reconnect stdin to the
# controlling terminal; if there isn't one, fail clearly instead of looping.
if [ ! -t 0 ]; then
  if [ -e /dev/tty ]; then
    exec </dev/tty
  else
    die "This installer needs an interactive terminal. Download it and run directly, e.g.:
    curl -fsSL ${REPO_URL%.git}/raw/main/scripts/install.sh -o install.sh && sudo bash install.sh"
  fi
fi

# Detect existing install
REINSTALL="no"
if [ -d "$PUBLISH_DIR" ] || systemctl list-unit-files 2>/dev/null | grep -q '^srxpanel.service'; then
  warn "An existing SRXPanel installation was detected."
  read -rp "Reinstall / upgrade in place? [y/N] " ans
  [[ "${ans,,}" == "y" ]] || die "Aborted."
  REINSTALL="yes"
fi

# ---------------------------------------------------------------------------
# Interactive prompts
# ---------------------------------------------------------------------------
info "Configuration"
read -rp "Panel domain (e.g. panel.myhost.com): " PANEL_DOMAIN
[ -n "$PANEL_DOMAIN" ] || die "Panel domain is required."

read -rp "Admin email: " ADMIN_EMAIL
[ -n "$ADMIN_EMAIL" ] || die "Admin email is required."

echo ""
read -sp "Enter admin password: " ADMIN_PASSWORD
echo ""
read -sp "Confirm admin password: " ADMIN_PASSWORD_CONFIRM
echo ""
if [ "$ADMIN_PASSWORD" != "$ADMIN_PASSWORD_CONFIRM" ]; then
    echo "❌ Passwords do not match. Exiting."
    exit 1
fi
[ -n "$ADMIN_PASSWORD" ] || die "Admin password cannot be empty."

read -rp "Let's Encrypt email [${ADMIN_EMAIL}]: " LE_EMAIL
LE_EMAIL="${LE_EMAIL:-$ADMIN_EMAIL}"

feature() { local q="$1" d="$2" a; read -rp "$q [${d}] " a; a="${a:-$d}"; [[ "${a,,}" == "y" ]] && echo "yes" || echo "no"; }
ENABLE_EMAIL=$(feature "Enable email server (Postfix/Dovecot)?" "y")
ENABLE_DNS=$(feature   "Enable DNS server (BIND9)?"            "y")
ENABLE_FTP=$(feature   "Enable FTP server (vsftpd)?"          "y")
ENABLE_PMA=$(feature   "Install phpMyAdmin?"                  "y")

# ---------------------------------------------------------------------------
# Package installation
# ---------------------------------------------------------------------------
info "Updating apt (this can take several minutes)…"
apt-get update -y
apt-get upgrade -y

echo "📦 [1/10] Installing system packages..."
apt-get install -y curl wget git software-properties-common unzip apt-transport-https ca-certificates jq
echo "✓ System packages done"

echo "📦 [2/10] Adding PHP repository..."
LC_ALL=C.UTF-8 add-apt-repository ppa:ondrej/php -y
apt-get update -y
echo "✓ PHP repository added"

echo "📦 [3/10] Installing Nginx..."
apt-get install -y nginx
echo "✓ Nginx done"

echo "📦 [4/10] Installing MySQL..."
apt-get install -y mysql-server
echo "✓ MySQL done"

echo "📦 [5/10] Installing PHP 8.2..."
apt-get install -y php8.2-fpm php8.2-cli php8.2-mysql php8.2-curl php8.2-gd php8.2-mbstring php8.2-xml php8.2-zip
# Extra PHP versions for multi-PHP hosting (best-effort; PPA added in step 2).
for v in 7.4 8.0 8.1 8.3; do
  apt-get install -y "php${v}-fpm" "php${v}-cli" "php${v}-mysql" || warn "php${v} not installed"
done
echo "✓ PHP done"

echo "📦 [6/10] Installing .NET 9..."
if ! command -v dotnet >/dev/null 2>&1; then
  wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
  dpkg -i /tmp/packages-microsoft-prod.deb
  apt-get update -y
  apt-get install -y dotnet-sdk-9.0
fi
echo "✓ .NET done"

echo "📦 [7/10] Installing Certbot..."
apt-get install -y certbot python3-certbot-nginx
echo "✓ Certbot done"

echo "📦 [8/10] Installing mail server..."
if [ "$ENABLE_EMAIL" = "yes" ]; then
  apt-get install -y postfix dovecot-core dovecot-imapd dovecot-pop3d
  echo "✓ Mail server done"
else
  echo "• Mail server skipped (disabled)"
fi

echo "📦 [9/10] Installing DNS + FTP..."
if [ "$ENABLE_DNS" = "yes" ]; then apt-get install -y bind9 bind9utils; fi
if [ "$ENABLE_FTP" = "yes" ]; then apt-get install -y vsftpd; fi
echo "✓ DNS + FTP done"

echo "📦 [10/10] Installing security tools..."
apt-get install -y fail2ban ufw quota quotatool clamav
echo "✓ Security tools done"

# Optional phpMyAdmin (best-effort; its debconf step can fail on some hosts).
[ "$ENABLE_PMA" = "yes" ] && apt-get install -y phpmyadmin || true
ok "Core packages installed"

# ---------------------------------------------------------------------------
# Directory structure
# ---------------------------------------------------------------------------
info "Creating directory structure…"
mkdir -p "$APP_DIR" "$CONFIG_DIR" "$CONFIG_DIR/ssl" "$LOG_DIR" "$BACKUP_DIR" "$VHOST_DIR"
chmod 700 "$CONFIG_DIR"

# ---------------------------------------------------------------------------
# MySQL setup
# ---------------------------------------------------------------------------
info "Configuring MySQL…"
DB_NAME="srxpanel"
DB_USER="srxpanel"
DB_PW="$(gen_pw)"

systemctl enable --now mysql 2>/dev/null || true

# Run each statement as its own non-interactive `-e` command. A heredoc on stdin
# can hang if the client waits for input; discrete -e calls with a connect timeout
# never block.
mysql -u root --connect-timeout=10 -e "CREATE DATABASE IF NOT EXISTS \`srxpanel\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
mysql -u root --connect-timeout=10 -e "CREATE USER IF NOT EXISTS 'srxpanel'@'localhost' IDENTIFIED BY '${DB_PW}';"
mysql -u root --connect-timeout=10 -e "GRANT ALL PRIVILEGES ON \`srxpanel\`.* TO 'srxpanel'@'localhost';"
mysql -u root --connect-timeout=10 -e "FLUSH PRIVILEGES;"

info "✓ MySQL configured"

# root now authenticates via the unix_socket plugin (no password is set), so the
# persisted "root password" is empty. Defined so the db.conf and appsettings
# references below don't trip `set -u`.
MYSQL_ROOT_PW=""

cat > "$CONFIG_DIR/db.conf" <<EOF
MYSQL_ROOT_PASSWORD=${MYSQL_ROOT_PW}
DB_NAME=${DB_NAME}
DB_USER=${DB_USER}
DB_PASSWORD=${DB_PW}
EOF
chmod 600 "$CONFIG_DIR/db.conf"
ok "MySQL configured (credentials saved to ${CONFIG_DIR}/db.conf)"

# ---------------------------------------------------------------------------
# Application deploy
# ---------------------------------------------------------------------------
info "Deploying the panel application…"
if [ -d "$APP_DIR/src/.git" ]; then
  git -C "$APP_DIR/src" pull --ff-only
else
  rm -rf "$APP_DIR/src"
  git clone --depth 1 "$REPO_URL" "$APP_DIR/src"
fi

cat > "$APP_DIR/src/appsettings.Production.json" <<EOF
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=${APP_DIR}/srxpanel.db"
  },
  "Version": "${SRX_VERSION}",
  "UpdateChannel": "stable",
  "SimulationMode": false,
  "Stripe": { "PublishableKey": "", "SecretKey": "", "WebhookSecret": "", "Currency": "usd" },
  "Panel": {
    "Hostname": "${PANEL_DOMAIN}",
    "LetsEncryptEmail": "${LE_EMAIL}",
    "Smtp": { "Host": "localhost", "Port": 587, "User": "", "Password": "", "From": "no-reply@${PANEL_DOMAIN}" },
    "MySql": { "Host": "localhost", "Port": 3306, "RootUser": "root", "RootPassword": "${MYSQL_ROOT_PW}" }
  },
  "Admin": { "Email": "${ADMIN_EMAIL}", "Password": "${ADMIN_PASSWORD}" }
}
EOF
chmod 600 "$APP_DIR/src/appsettings.Production.json"

info "Publishing (dotnet publish -c Release)…"
dotnet publish "$APP_DIR/src/SRXPanel.csproj" -c Release -o "$PUBLISH_DIR"
cp "$APP_DIR/src/appsettings.Production.json" "$PUBLISH_DIR/appsettings.Production.json"

# Migrations run automatically on startup (Program.cs calls Database.Migrate()).
chown -R www-data:www-data "$APP_DIR" "$LOG_DIR" "$BACKUP_DIR" "$VHOST_DIR"
ok "Application published"

# ---------------------------------------------------------------------------
# systemd service
# ---------------------------------------------------------------------------
info "Creating systemd service…"

# Store the operator-chosen admin password in a root-only env file and pass it to
# the app via EnvironmentFile. The seeder reads SRXPANEL_ADMIN_PASSWORD when it
# creates the initial admin account.
cat > "$CONFIG_DIR/admin.env" <<EOF
SRXPANEL_ADMIN_PASSWORD=${ADMIN_PASSWORD}
EOF
chmod 600 "$CONFIG_DIR/admin.env"

cat > /etc/systemd/system/srxpanel.service <<EOF
[Unit]
Description=SRXPanel Hosting Control Panel
After=network.target mysql.service

[Service]
WorkingDirectory=${PUBLISH_DIR}
ExecStart=/usr/bin/dotnet ${PUBLISH_DIR}/SRXPanel.dll
Restart=always
RestartSec=10
User=www-data
EnvironmentFile=${CONFIG_DIR}/admin.env
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000
StandardOutput=syslog
StandardError=syslog
SyslogIdentifier=srxpanel

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable srxpanel
systemctl restart srxpanel
ok "srxpanel.service started"

# ---------------------------------------------------------------------------
# Nginx reverse proxy
# ---------------------------------------------------------------------------
info "Configuring Nginx reverse proxy…"
cat > /etc/nginx/sites-available/srxpanel.conf <<EOF
limit_req_zone \$binary_remote_addr zone=srx_login:10m rate=10r/m;

server {
    listen 80;
    server_name ${PANEL_DOMAIN};

    # Security headers
    add_header X-Frame-Options SAMEORIGIN always;
    add_header X-Content-Type-Options nosniff always;
    add_header Referrer-Policy strict-origin-when-cross-origin always;

    gzip on;
    gzip_types text/plain text/css application/json application/javascript text/xml application/xml image/svg+xml;

    # Block common attack patterns
    location ~* (\.env|\.git|wp-login\.php|xmlrpc\.php)$ { return 403; }

    location /Account/Login {
        limit_req zone=srx_login burst=5 nodelay;
        proxy_pass http://localhost:5000;
        include proxy_params;
    }

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        client_max_body_size 100M;
    }
}
EOF

ln -sf /etc/nginx/sites-available/srxpanel.conf /etc/nginx/sites-enabled/srxpanel.conf
rm -f /etc/nginx/sites-enabled/default
nginx -t && systemctl reload nginx
ok "Nginx configured"

info "Requesting Let's Encrypt certificate…"
certbot --nginx -d "${PANEL_DOMAIN}" --non-interactive --agree-tos -m "${LE_EMAIL}" --redirect \
  || warn "Certbot failed — check DNS for ${PANEL_DOMAIN} and re-run: certbot --nginx -d ${PANEL_DOMAIN}"

# ---------------------------------------------------------------------------
# Firewall (UFW)
# ---------------------------------------------------------------------------
info "Configuring firewall…"
ufw --force reset >/dev/null
ufw default deny incoming
ufw default allow outgoing
ufw allow ssh
ufw allow http
ufw allow https
if [ "$ENABLE_FTP" = "yes" ]; then ufw allow 21/tcp; fi
if [ "$ENABLE_EMAIL" = "yes" ]; then ufw allow 25/tcp; ufw allow 143/tcp; ufw allow 993/tcp; ufw allow 110/tcp; ufw allow 995/tcp; fi
if [ "$ENABLE_DNS" = "yes" ]; then ufw allow 53; fi
ufw --force enable
ok "Firewall enabled"

# ---------------------------------------------------------------------------
# Fail2ban
# ---------------------------------------------------------------------------
info "Configuring Fail2ban…"
cat > /etc/fail2ban/jail.d/srxpanel.conf <<'EOF'
[sshd]
enabled = true
maxretry = 5

[nginx-http-auth]
enabled = true

[nginx-badbots]
enabled  = true
port     = http,https
filter   = nginx-badbots
logpath  = /var/log/nginx/access.log
maxretry = 2
EOF
if [ "$ENABLE_EMAIL" = "yes" ]; then
  cat >> /etc/fail2ban/jail.d/srxpanel.conf <<'EOF'

[postfix]
enabled = true

[dovecot]
enabled = true
EOF
fi
systemctl enable --now fail2ban
systemctl restart fail2ban || true
ok "Fail2ban configured"

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------
PROTO="https"
curl -fsk "https://${PANEL_DOMAIN}/api/health" >/dev/null 2>&1 || PROTO="http"

cat <<EOF

╔═══════════════════════════════════════════════════╗
║        SRXPanel installed successfully!           ║
╠═══════════════════════════════════════════════════╣
║ Panel URL:  ${PROTO}://${PANEL_DOMAIN}
║ Username:   admin
║ Password:   (the admin password you entered)
║
║ MySQL Root: saved to ${CONFIG_DIR}/db.conf
║
║ Next steps:
║  1. Login and configure Stripe (Admin → Platform Settings)
║  2. Create hosting packages
║  3. Add nameserver DNS records
╚═══════════════════════════════════════════════════╝

Service:  systemctl status srxpanel
Logs:     journalctl -u srxpanel -f
Health:   curl ${PROTO}://${PANEL_DOMAIN}/api/health

EOF
ok "Installation complete."
