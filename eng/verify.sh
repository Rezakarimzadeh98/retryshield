#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

dotnet restore
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release --collect:"XPlat Code Coverage"

(
  cd web/admin
  npm ci
  npm test -- --run
  npm run build
)

if command -v docker >/dev/null 2>&1; then
  docker compose -f deploy/compose.yml config --quiet
fi

echo "RetryShield verification passed."
