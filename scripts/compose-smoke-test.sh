#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="backup-gateway-smoke-$$"
work_dir="$(mktemp -d)"
port="${BACKUP_GATEWAY_SMOKE_PORT:-18080}"
app_uid="${BACKUP_GATEWAY_CONTAINER_UID:-1654}"
compose=(docker compose -f "$repo_root/compose.yaml" --project-directory "$repo_root" -p "$project")

cleanup() {
    "${compose[@]}" down --volumes --remove-orphans >/dev/null 2>&1 || true
    rm -rf "$work_dir"
}
trap cleanup EXIT

command -v docker >/dev/null
command -v curl >/dev/null
command -v openssl >/dev/null
"${compose[@]}" version >/dev/null

jwt_key="$work_dir/jwt-signing-key.pem"
bootstrap_file="$work_dir/bootstrap-admin-credential"
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$jwt_key" >/dev/null 2>&1
administrator_credential="$(openssl rand -base64 36 | tr -d '\n')"
printf '%s' "$administrator_credential" > "$bootstrap_file"
chmod 700 "$work_dir"
chmod 600 "$jwt_key" "$bootstrap_file"
if command -v setfacl >/dev/null; then
    setfacl -m "u:${app_uid}:x" "$work_dir"
    setfacl -m "u:${app_uid}:r" "$jwt_key" "$bootstrap_file"
else
    # Fallback for minimal hosts without POSIX ACL tooling. The directory name is random and
    # removed on exit; production deployments should use restrictive ownership or ACLs.
    chmod 711 "$work_dir"
    chmod 604 "$jwt_key" "$bootstrap_file"
fi

export POSTGRES_PASSWORD="$(openssl rand -base64 36 | tr -d '\n')"
export BACKUP_GATEWAY_PORT="$port"
export BACKUP_GATEWAY_JWT_KEY_FILE="$jwt_key"
export BACKUP_GATEWAY_BOOTSTRAP_CREDENTIAL_FILE="$bootstrap_file"
export BACKUP_GATEWAY_BOOTSTRAP_ADMIN="smoke-admin"

"${compose[@]}" config >/dev/null
"${compose[@]}" up --detach --build

base_url="http://127.0.0.1:${port}"
for _ in $(seq 1 60); do
    if curl --fail --silent --show-error "$base_url/health/ready" >/dev/null 2>&1; then
        break
    fi
    sleep 1
done

curl --fail --silent --show-error "$base_url/health/live" >/dev/null
curl --fail --silent --show-error "$base_url/health/ready" >/dev/null
curl --fail --silent --show-error "$base_url/metrics" | grep --quiet '^# HELP backup_gateway_held_leases'
curl --fail --silent --show-error "$base_url/openapi/v1.yaml" | grep --quiet '^openapi: 3.1.0$'

auth_response="$(curl --fail --silent --show-error \
    -H 'Content-Type: application/json' \
    -d "{\"username\":\"smoke-admin\",\"credential\":\"${administrator_credential}\"}" \
    "$base_url/api/v1/auth/token")"
grep --quiet '"accessToken"' <<<"$auth_response"

echo "Backup Gateway Compose smoke test passed."
