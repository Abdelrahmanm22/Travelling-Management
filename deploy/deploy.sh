#!/usr/bin/env bash
# Installed as /srv/travel/deploy.sh (root-owned). GitHub Actions reaches it through the "deploy" user,
# whose only SSH key is pinned to `sudo -n /srv/travel/deploy.sh <args>` — so the CI key can run this
# and nothing else.
#
#   deploy.sh <prod|dev> ghcr.io/abdelrahmanm22/travelling-management@sha256:<digest>
#   (a short-lived GITHUB_TOKEN for pulling arrives on stdin)
set -euo pipefail

[[ $# -eq 2 ]] || { echo "usage: deploy.sh <prod|dev> <image@sha256:digest>" >&2; exit 2; }
TARGET=$1 IMAGE=$2

case "$TARGET" in
  prod) SERVICE=app-prod VAR=PROD_IMAGE HOST=sys.mustatravel.com.br     DB=TravelDB ;;
  dev)  SERVICE=app-dev  VAR=DEV_IMAGE  HOST=dev-sys.mustatravel.com.br DB= ;;
  *) echo "target must be prod or dev" >&2; exit 2 ;;
esac
[[ "$IMAGE" =~ ^ghcr\.io/abdelrahmanm22/travelling-management@sha256:[0-9a-f]{64}$ ]] \
  || { echo "image must be a ghcr.io digest reference" >&2; exit 2; }

cd /srv/travel
CONTAINER="travel-$SERVICE-1"
log() { echo "[$(date -u +%H:%M:%S)] $*"; }

if [[ ! -t 0 ]]; then TOKEN=$(cat); fi
if [[ -n "${TOKEN:-}" ]]; then
  echo "$TOKEN" | docker login ghcr.io -u github-actions --password-stdin >/dev/null
  trap 'docker logout ghcr.io >/dev/null 2>&1 || true' EXIT
fi

log "pulling $IMAGE"
docker pull -q "$IMAGE" >/dev/null

# Production only: a migration is applied on startup, so take a restorable copy first. Keeps the last 10.
if [[ -n "$DB" ]]; then
  mkdir -p backups/pre-deploy && chown 10001 backups/pre-deploy
  FILE="pre-deploy/${DB}_$(date -u +%Y%m%d-%H%M%S).bak"
  log "backing up $DB to backups/$FILE"
  docker exec travel-mssql-1 bash -c "/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P \"\$MSSQL_SA_PASSWORD\" -b -Q \"BACKUP DATABASE [$DB] TO DISK = N'/backups/$FILE' WITH INIT, CHECKSUM\"" >/dev/null
  ls -1t backups/pre-deploy/*.bak | tail -n +11 | xargs -r rm --
fi

PREVIOUS=$(grep "^$VAR=" .env | cut -d= -f2-)
switch_to() {
  sed -i "s|^$VAR=.*|$VAR=$1|" .env
  docker compose up -d --no-deps "$SERVICE" >/dev/null 2>&1
}

# Program.cs logs a failed migration and keeps starting, so /healthz alone would call it healthy.
healthy() {
  local started; started=$(docker inspect -f '{{.State.StartedAt}}' "$CONTAINER")
  for _ in $(seq 1 40); do
    if docker logs --since "$started" "$CONTAINER" 2>&1 | grep -q "error occurred while applying database migrations"; then
      log "startup migration failed"; return 1
    fi
    curl -fsS -m 5 --resolve "$HOST:443:127.0.0.1" "https://$HOST/healthz" >/dev/null 2>&1 && return 0
    sleep 3
  done
  log "no healthy response within 2 minutes"; return 1
}

log "switching $SERVICE"
switch_to "$IMAGE"
if healthy; then
  log "$SERVICE is healthy on the new image"
  docker image prune -f >/dev/null
  exit 0
fi

docker logs --tail 40 "$CONTAINER" 2>&1 || true
log "rolling back $SERVICE to $PREVIOUS"
switch_to "$PREVIOUS"
if healthy; then log "rollback is healthy"; else log "ROLLBACK IS UNHEALTHY TOO - needs a human"; fi
exit 1
