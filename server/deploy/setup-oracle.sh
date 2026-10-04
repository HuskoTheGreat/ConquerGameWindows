#!/usr/bin/env bash
# One-time setup for an Oracle Cloud Always Free Ubuntu 22.04/24.04 VM (Ampere A1 arm64 or AMD micro x64).
# Run as a sudo-capable user AFTER you have confirmed you can SSH in with your key (this turns passwords off).
#
#   sudo CONQUER_DOMAIN=conquer.example.com ./setup-oracle.sh            # game server only
#   sudo CONQUER_DOMAIN=conquer.example.com BOTS=1 ./setup-oracle.sh     # plus the local LLM for bots (Ampere only)
#
# Also open TCP 80 and 443 in the VCN security list (or NSG) in the Oracle console: Oracle has two firewalls.
set -euo pipefail

: "${CONQUER_DOMAIN:?Set CONQUER_DOMAIN to the DNS name pointing at this VM}"
BOTS="${BOTS:-0}"
# Apache-2.0 licensed, ~1 GB, fast enough on 2 Ampere cores for one-sentence quips.
MODEL_URL="${MODEL_URL:-https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf}"
HERE="$(cd "$(dirname "$0")" && pwd)"

echo "== Packages and automatic security updates"
apt-get update
DEBIAN_FRONTEND=noninteractive apt-get -y upgrade
DEBIAN_FRONTEND=noninteractive apt-get -y install unattended-upgrades iptables-persistent \
    debian-keyring debian-archive-keyring apt-transport-https curl gnupg
dpkg-reconfigure -f noninteractive unattended-upgrades

# .NET 8 runtime from Ubuntu's own archive (patched by unattended-upgrades along with everything else).
DEBIAN_FRONTEND=noninteractive apt-get -y install aspnetcore-runtime-8.0

if ! command -v caddy >/dev/null; then
    curl -1sLf https://dl.cloudsmith.io/public/caddy/stable/gpg.key | gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
    curl -1sLf https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt > /etc/apt/sources.list.d/caddy-stable.list
    apt-get update
    DEBIAN_FRONTEND=noninteractive apt-get -y install caddy
fi

echo "== Swap (the 1 GB AMD shape needs it; harmless elsewhere)"
if ! swapon --show | grep -q /swapfile; then
    fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
    grep -q /swapfile /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi

echo "== SSH: keys only, no root login"
cat > /etc/ssh/sshd_config.d/99-conquer-hardening.conf <<'CONF'
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin no
CONF
systemctl reload ssh || systemctl reload sshd

echo "== VM firewall: allow 80 and 443 (22 is already open on Oracle images)"
# Oracle's images end INPUT with a REJECT rule, so insert ahead of it. Everything else stays blocked.
for port in 80 443; do
    if ! iptables -C INPUT -p tcp --dport "$port" -m conntrack --ctstate NEW -j ACCEPT 2>/dev/null; then
        reject=$(iptables -L INPUT --line-numbers -n | awk '$2 == "REJECT" { print $1; exit }')
        if [ -n "$reject" ]; then
            iptables -I INPUT "$reject" -p tcp --dport "$port" -m conntrack --ctstate NEW -j ACCEPT
        else
            iptables -A INPUT -p tcp --dport "$port" -m conntrack --ctstate NEW -j ACCEPT
        fi
    fi
done
netfilter-persistent save

echo "== Game server user and files"
id conquer >/dev/null 2>&1 || useradd --system --home /opt/conquer --shell /usr/sbin/nologin conquer
mkdir -p /opt/conquer/server
if [ -d "$HERE/../publish" ]; then
    cp -r "$HERE/../publish/." /opt/conquer/server/
fi
chown -R root:conquer /opt/conquer/server
chmod -R o-rwx /opt/conquer/server
install -m 644 "$HERE/conquer-server.service" /etc/systemd/system/conquer-server.service

if [ "$BOTS" = "1" ]; then
    echo "== Local LLM for bots (llama.cpp, loopback only)"
    DEBIAN_FRONTEND=noninteractive apt-get -y install git cmake build-essential
    id conquer-llm >/dev/null 2>&1 || useradd --system --home /opt/conquer/models --shell /usr/sbin/nologin conquer-llm
    if [ ! -d /opt/conquer/llama.cpp ]; then
        git clone --depth 1 https://github.com/ggml-org/llama.cpp /opt/conquer/llama.cpp
    fi
    cmake -S /opt/conquer/llama.cpp -B /opt/conquer/llama.cpp/build -DCMAKE_BUILD_TYPE=Release -DLLAMA_CURL=OFF
    cmake --build /opt/conquer/llama.cpp/build --target llama-server -j "$(nproc)"
    mkdir -p /opt/conquer/models
    [ -f /opt/conquer/models/model.gguf ] || curl -fL "$MODEL_URL" -o /opt/conquer/models/model.gguf
    chown -R root:conquer-llm /opt/conquer/models && chmod -R o-rwx /opt/conquer/models
    install -m 644 "$HERE/conquer-llm.service" /etc/systemd/system/conquer-llm.service
    systemctl daemon-reload
    systemctl enable --now conquer-llm
    # Turn bots on in the game server.
    mkdir -p /etc/systemd/system/conquer-server.service.d
    cat > /etc/systemd/system/conquer-server.service.d/bots.conf <<'CONF'
[Service]
Environment=Conquer__Bots__Enabled=true
Environment=Conquer__Bots__BaseUrl=http://127.0.0.1:8081
CONF
fi

echo "== Caddy (HTTPS) and the game server"
install -m 644 "$HERE/Caddyfile" /etc/caddy/Caddyfile
grep -q '^CONQUER_DOMAIN=' /etc/default/caddy 2>/dev/null && sed -i "s/^CONQUER_DOMAIN=.*/CONQUER_DOMAIN=$CONQUER_DOMAIN/" /etc/default/caddy \
    || echo "CONQUER_DOMAIN=$CONQUER_DOMAIN" >> /etc/default/caddy
mkdir -p /etc/systemd/system/caddy.service.d
printf '[Service]\nEnvironmentFile=/etc/default/caddy\n' > /etc/systemd/system/caddy.service.d/env.conf
systemctl daemon-reload
systemctl enable --now conquer-server
systemctl restart caddy

echo "Done. Check: curl https://$CONQUER_DOMAIN/healthz"
