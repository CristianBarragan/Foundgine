$ErrorActionPreference = "Stop"

$composeFile = "docker-compose.postgres.yml"
$connectionString = "Host=localhost;Port=55432;Database=foundgine_e2e;Username=foundgine;Password=foundgine"

Write-Host "Checking Docker..."
docker version | Out-Null

Write-Host "Starting PostgreSQL 17..."
docker compose -f $composeFile up -d postgres

try {
    Write-Host "Waiting for PostgreSQL..."
    $ready = $false

    for ($i = 1; $i -le 60; $i++) {
        $result = docker compose -f $composeFile exec -T postgres pg_isready -U foundgine -d foundgine_e2e 2>$null
        if ($LASTEXITCODE -eq 0) {
            $ready = $true
            break
        }
        Start-Sleep -Seconds 1
    }

    if (-not $ready) {
        docker compose -f $composeFile ps
        docker compose -f $composeFile logs postgres
        throw "PostgreSQL did not become ready in time."
    }

    Write-Host "PostgreSQL version:"
    docker compose -f $composeFile exec -T postgres psql -U foundgine -d foundgine_e2e -Atc "SHOW server_version;"

    $env:FOUNDGINE_POSTGRES_CONNECTION_STRING = $connectionString

    Write-Host "Running PostgreSQL E2E tests..."
    dotnet test .\src\csharp\tests\Foundgine.E2E.Tests\Foundgine.E2E.Tests.csproj `
        --configuration Release `
        --filter "FullyQualifiedName~Foundgine.E2E.Tests" `
        --logger "console;verbosity=normal"

    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL E2E tests failed."
    }
}
finally {
    Write-Host "Leaving PostgreSQL 17 running. Database/schema lifecycle is owned by PostgreSQL init scripts."
    Write-Host "To deliberately recreate the database from init state, run:"
    Write-Host "  docker compose -f $composeFile down --volumes --remove-orphans"
}
