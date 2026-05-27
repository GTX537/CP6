#!/usr/bin/env bash
set -euo pipefail

REPO_URL="${REPO_URL:-https://github.com/GTX537/CP6.git}"
APP_DIR="${APP_DIR:-/opt/cp6}"
BRANCH="${BRANCH:-main}"

log() {
  printf '[%s] %s\n' "$(date '+%F %T')" "$1"
}

if [ "${EUID}" -ne 0 ]; then
  echo "Please run as root." >&2
  exit 1
fi

export DEBIAN_FRONTEND=noninteractive

log "Updating apt metadata"
apt-get update -qq

if ! command -v git >/dev/null 2>&1; then
  log "Installing git"
  apt-get install -y -qq git
fi

if ! command -v docker >/dev/null 2>&1; then
  log "Installing Docker"
  apt-get install -y -qq ca-certificates curl gnupg
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
    | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
  chmod a+r /etc/apt/keyrings/docker.gpg

  . /etc/os-release
  echo \
    "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable" \
    > /etc/apt/sources.list.d/docker.list

  apt-get update -qq
  apt-get install -y -qq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
  systemctl enable --now docker
else
  log "Docker already installed: $(docker --version)"
fi

if [ ! -d "${APP_DIR}/.git" ]; then
  log "Cloning repository into ${APP_DIR}"
  git clone --branch "${BRANCH}" "${REPO_URL}" "${APP_DIR}"
else
  log "Updating repository in ${APP_DIR}"
  git -C "${APP_DIR}" fetch origin
  git -C "${APP_DIR}" checkout "${BRANCH}"
  git -C "${APP_DIR}" pull --ff-only origin "${BRANCH}"
fi

cd "${APP_DIR}"

log "Stopping old containers"
docker compose down --remove-orphans || true

log "Building and starting services"
docker compose up -d --build

log "Current container status"
docker compose ps

log "Deployment finished"
echo "Frontend:  http://$(hostname -I | awk '{print $1}'):8080"
echo "API:       http://$(hostname -I | awk '{print $1}'):9991/swagger"
echo "RabbitMQ:  http://$(hostname -I | awk '{print $1}'):15672"
