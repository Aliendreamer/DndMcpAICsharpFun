#!/usr/bin/env bash
set -euo pipefail

ENV=${1:?"Usage: ./start.sh <Development|Production>"}

if [[ "$ENV" != "Development" && "$ENV" != "Production" ]]; then
  echo "Error: environment must be Development or Production"
  exit 1
fi

export ASPNETCORE_ENVIRONMENT="$ENV"
docker compose up --build -d
