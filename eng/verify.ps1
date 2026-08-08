$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    dotnet restore
    dotnet build --no-restore --configuration Release
    dotnet test --no-build --configuration Release --collect:"XPlat Code Coverage"

    Push-Location "web/admin"
    try {
        npm ci
        npm test -- --run
        npm run build
    }
    finally {
        Pop-Location
    }

    if (Get-Command docker -ErrorAction SilentlyContinue) {
        docker compose -f deploy/compose.yml config --quiet
    }

    Write-Host "RetryShield verification passed." -ForegroundColor Green
}
finally {
    Pop-Location
}
