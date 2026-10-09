#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
#
# Installs the published server as a systemd unit (deploy/systemd/nexopbxd.service) and proves that the unit starts
# (Type=notify), answers health checks, and stops cleanly. Needs root (uses sudo). Used by CI.
#   scripts/ci/smoke-systemd.sh <published-directory>
set -euo pipefail

bin="$(cd "${1:?usage: smoke-systemd.sh <published-directory>}" && pwd)"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
http_port=18082
grpc_port=18083

dump() { sudo journalctl -u nexopbxd --no-pager -n 80 || true; }
trap 'status=$?; if [[ $status -ne 0 ]]; then echo "--- journal"; dump; fi; sudo systemctl stop nexopbxd 2>/dev/null || true; exit $status' EXIT

sudo useradd --system --home-dir /var/lib/nexopbx --shell /usr/sbin/nologin nexopbx 2>/dev/null || true
sudo rm -rf /opt/nexopbx && sudo mkdir -p /opt/nexopbx /etc/nexopbx
sudo cp -r "$bin"/. /opt/nexopbx/
sudo chmod +x /opt/nexopbx/nexopbxd
sudo cp "$root/deploy/systemd/nexopbxd.service" /etc/systemd/system/nexopbxd.service
sudo tee /etc/nexopbx/nexopbx.yaml >/dev/null <<YAML
server:
  http_listen: 127.0.0.1:${http_port}
  grpc_listen: 127.0.0.1:${grpc_port}
logging:
  level: Information
YAML
sudo chown -R root:nexopbx /etc/nexopbx && sudo chmod 750 /etc/nexopbx && sudo chmod 640 /etc/nexopbx/nexopbx.yaml

sudo systemctl daemon-reload
echo "==> systemctl start (Type=notify: returns once the host reports ready)"
sudo timeout 90 systemctl start nexopbxd
sudo systemctl is-active nexopbxd

echo "==> health"
for _ in $(seq 1 30); do curl -fsS "http://127.0.0.1:${http_port}/health/ready" >/dev/null 2>&1 && break; sleep 1; done
curl -fsS "http://127.0.0.1:${http_port}/health/ready"; echo
curl -fsS "http://127.0.0.1:${http_port}/health/live"; echo

echo "==> data was created under the state directory, owned by the service user"
sudo test -f /var/lib/nexopbx/nexopbx.db
test "$(sudo stat -c %U /var/lib/nexopbx/nexopbx.db)" = "nexopbx"

echo "==> the unit refuses a second instance on the same data directory"
if sudo -u nexopbx /opt/nexopbx/nexopbxd service; then echo "second instance unexpectedly ran"; exit 1; else code=$?; test "$code" -eq 3; fi

echo "==> systemctl stop"
sudo timeout 90 systemctl stop nexopbxd
if sudo systemctl is-active --quiet nexopbxd; then echo "still active"; exit 1; fi
test "$(sudo systemctl show nexopbxd -p Result --value)" = "success"
echo "==> OK: systemd smoke test passed"
