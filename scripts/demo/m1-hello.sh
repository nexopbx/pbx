#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
#
# Milestone 1 demo: a fresh NexoPBX install, from nothing to a client reading live events through the public API.
#
#   1. publish the server and the sample client (or reuse NEXOPBX_BIN_DIR / NEXOPBX_SAMPLE_DIR)
#   2. create the first Owner and an API token with the CLI
#   3. start the server on loopback ports and wait until it is ready
#   4. call REST, then run the sample client self-test over native gRPC (it also checks the live event stream)
#   5. stop the server and make sure it exits cleanly
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d)"
http_port="${NEXOPBX_DEMO_HTTP_PORT:-18080}"
grpc_port="${NEXOPBX_DEMO_GRPC_PORT:-18081}"
server_pid=""

cleanup() {
  if [[ -n "$server_pid" ]] && kill -0 "$server_pid" 2>/dev/null; then
    kill "$server_pid" 2>/dev/null || true
    wait "$server_pid" 2>/dev/null || true
  fi
  rm -rf "$work"
}
trap cleanup EXIT

bin="${NEXOPBX_BIN_DIR:-}"
sample="${NEXOPBX_SAMPLE_DIR:-}"
if [[ -z "$bin" ]]; then
  echo "==> Publishing the server"
  dotnet publish "$root/src/NexoPBX.App" -c Release -o "$work/bin" --nologo -v q
  bin="$work/bin"
fi
if [[ -z "$sample" ]]; then
  echo "==> Publishing the sample client"
  dotnet publish "$root/samples/NexoPBX.SampleClient" -c Release -o "$work/sample" --nologo -v q
  sample="$work/sample"
fi

export NEXOPBX_DATA_DIR="$work/data"
export NEXOPBX_CONFIG="$work/nexopbx.yaml"
cat > "$NEXOPBX_CONFIG" <<YAML
server:
  http_listen: 127.0.0.1:${http_port}
  grpc_listen: 127.0.0.1:${grpc_port}
logging:
  level: Warning
YAML

echo "==> Creating the first Owner and an API token (no 'db migrate' needed on a fresh install)"
printf 'correct horse battery staple\n' | "$bin/nexopbxd" user bootstrap --username admin --password-stdin
token="$("$bin/nexopbxd" token create --user admin --name demo --scope system.read --scope users.read --scope events.subscribe 2>/dev/null)"

echo "==> Starting the server"
"$bin/nexopbxd" run > "$work/server.log" 2>&1 &
server_pid=$!
for _ in $(seq 1 60); do
  if curl -fsS "http://127.0.0.1:${http_port}/health/ready" >/dev/null 2>&1; then break; fi
  if ! kill -0 "$server_pid" 2>/dev/null; then echo "Server exited early:"; cat "$work/server.log"; exit 1; fi
  sleep 0.5
done
curl -fsS "http://127.0.0.1:${http_port}/health/ready" >/dev/null || { echo "Server never became ready"; cat "$work/server.log"; exit 1; }

echo "==> REST: GET /v1/system/info"
curl -fsS -H "Authorization: Bearer ${token}" "http://127.0.0.1:${http_port}/v1/system/info"
echo
echo "==> Sample client self-test over native gRPC"
NEXOPBX_URL="http://127.0.0.1:${grpc_port}" NEXOPBX_TOKEN="$token" dotnet "$sample/NexoPBX.SampleClient.dll" --selftest

echo "==> Stopping the server"
kill -TERM "$server_pid"
for _ in $(seq 1 40); do kill -0 "$server_pid" 2>/dev/null || break; sleep 0.25; done
if kill -0 "$server_pid" 2>/dev/null; then echo "Server did not stop in time"; exit 1; fi
wait "$server_pid" || true
server_pid=""
echo "==> OK: M1 demo passed"
