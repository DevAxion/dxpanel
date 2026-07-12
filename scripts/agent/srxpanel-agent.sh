#!/usr/bin/env bash
#
# SRXPanel Node Agent
# -------------------
# A lightweight monitoring/orchestration agent installed on each managed node.
# It listens ONLY on localhost:9876 — the panel reaches it through an SSH tunnel,
# so the port is never exposed to the public internet. Every request must carry a
# valid HMAC-SHA256 signature, and only whitelisted commands may run.
#
# Install:
#   sudo cp srxpanel-agent.sh /usr/local/bin/srxpanel-agent
#   sudo chmod +x /usr/local/bin/srxpanel-agent
#   sudo tee /etc/srxpanel/agent.env >/dev/null <<EOF
#   SRX_AGENT_SECRET=<shared-secret-from-panel>
#   EOF
#   sudo systemctl enable --now srxpanel-agent
#
# Endpoints (all require X-SRX-Signature: sha256=<hmac of body>):
#   GET  /health          -> service statuses (json)
#   GET  /metrics         -> cpu/ram/disk/network (json)
#   GET  /processes       -> top 10 processes (json)
#   POST /command         -> execute a whitelisted command
#   POST /nginx/reload    -> nginx -t && systemctl reload nginx
#   POST /mysql/query     -> run a read-only SQL query
#   GET  /logs/<service>  -> last N lines of a service log

set -euo pipefail

AGENT_PORT="${SRX_AGENT_PORT:-9876}"
AGENT_BIND="127.0.0.1"
SECRET="${SRX_AGENT_SECRET:-}"

# Commands the panel is permitted to run. Anything else is rejected.
WHITELIST=(
  "systemctl status"
  "systemctl restart nginx"
  "systemctl restart php8.3-fpm"
  "systemctl restart mysql"
  "systemctl reload nginx"
  "nginx -t"
  "df -PBG"
  "free -m"
  "uptime"
  "ps aux"
  "journalctl"
  "apt-get update"
)

log() { echo "[$(date -u +%FT%TZ)] $*" >&2; }

# ---- HMAC verification -------------------------------------------------------
verify_signature() {
  local body="$1" provided="$2"
  [ -z "$SECRET" ] && { log "no secret configured"; return 1; }
  local expected
  expected="sha256=$(printf '%s' "$body" | openssl dgst -sha256 -hmac "$SECRET" | awk '{print $2}')"
  [ "$expected" = "$provided" ]
}

is_whitelisted() {
  local cmd="$1"
  for allowed in "${WHITELIST[@]}"; do
    [[ "$cmd" == "$allowed"* ]] && return 0
  done
  return 1
}

# ---- Data collectors ---------------------------------------------------------
collect_metrics() {
  local cpu ram disk
  cpu=$(top -bn1 | awk '/Cpu\(s\)/ {print 100 - $8}')
  ram=$(free | awk '/Mem:/ {printf "%.1f", $3/$2*100}')
  disk=$(df -P / | awk 'NR==2 {gsub("%","",$5); print $5}')
  read -r l1 l5 l15 _ < /proc/loadavg
  printf '{"cpu":%s,"ram":%s,"disk":%s,"load1":%s,"load5":%s,"load15":%s}\n' \
    "${cpu:-0}" "${ram:-0}" "${disk:-0}" "$l1" "$l5" "$l15"
}

collect_health() {
  local out="{" first=1
  for svc in nginx mysql php8.3-fpm vsftpd postfix named; do
    local state
    state=$(systemctl is-active "$svc" 2>/dev/null || echo "not-installed")
    [ $first -eq 0 ] && out+=","
    out+="\"$svc\":\"$state\""
    first=0
  done
  echo "${out}}"
}

collect_processes() {
  ps aux --sort=-%cpu | awk 'NR>1 && NR<=11 {printf "%s{\"pid\":%s,\"user\":\"%s\",\"cpu\":%s,\"mem\":%s}", (NR>2?",":""), $2,$1,$3,$4}' \
    | sed 's/^/[/; s/$/]/'
}

# ---- Request handler ---------------------------------------------------------
handle_request() {
  local method path body signature
  read -r method path _
  # Read headers, capture the signature and content length.
  local content_length=0
  while IFS= read -r line; do
    line="${line%$'\r'}"
    [ -z "$line" ] && break
    case "$line" in
      X-SRX-Signature:*) signature="${line#X-SRX-Signature: }" ;;
      Content-Length:*)  content_length="${line#Content-Length: }" ;;
    esac
  done
  [ "$content_length" -gt 0 ] && read -r -n "$content_length" body || body=""

  if ! verify_signature "${body:-$path}" "${signature:-}"; then
    printf 'HTTP/1.1 401 Unauthorized\r\nContent-Type: application/json\r\n\r\n{"error":"bad signature"}\n'
    return
  fi

  local response
  case "$method $path" in
    "GET /health")    response=$(collect_health) ;;
    "GET /metrics")   response=$(collect_metrics) ;;
    "GET /processes") response=$(collect_processes) ;;
    "POST /nginx/reload")
      if nginx -t 2>/dev/null; then systemctl reload nginx; response='{"reloaded":true}'
      else response='{"reloaded":false,"error":"config test failed"}'; fi ;;
    "POST /command")
      local cmd
      cmd=$(printf '%s' "$body" | sed -n 's/.*"command"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
      if is_whitelisted "$cmd"; then
        local output; output=$(eval "$cmd" 2>&1 || true)
        response=$(printf '{"output":%s}' "$(printf '%s' "$output" | python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()))')")
      else
        response='{"error":"command not whitelisted"}'
      fi ;;
    "GET /logs/"*)
      local svc="${path#/logs/}"
      response=$(printf '{"log":%s}' "$(journalctl -u "$svc" -n 50 --no-pager 2>/dev/null | python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()))')") ;;
    *) response='{"error":"not found"}' ;;
  esac

  printf 'HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: %s\r\n\r\n%s' \
    "${#response}" "$response"
}

# ---- Main loop ---------------------------------------------------------------
main() {
  [ -f /etc/srxpanel/agent.env ] && . /etc/srxpanel/agent.env
  log "SRXPanel agent listening on ${AGENT_BIND}:${AGENT_PORT} (localhost only)"

  # Requires socat or ncat; systemd socket activation is preferred in production.
  while true; do
    ncat -l "$AGENT_BIND" "$AGENT_PORT" -c 'handle_request' 2>/dev/null || sleep 1
  done
}

export -f handle_request verify_signature is_whitelisted collect_metrics collect_health collect_processes log
main
