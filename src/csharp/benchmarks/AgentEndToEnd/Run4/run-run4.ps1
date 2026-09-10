[CmdletBinding()]
param([ValidateSet('agent','protocol','both')][string]$Mode='both',[int[]]$CustomerCounts=@(10,100,1000,10000),[int[]]$Concurrency=@(8,16,32,64),[int[]]$RunsPerTier=@(30,30,30,30),[int]$Warmups=5,[switch]$Publish)
$ErrorActionPreference='Stop'
# PowerShell 7.3+ promotes anything a native command (docker, docker compose,
# etc.) writes to stderr into an ErrorRecord and, with $ErrorActionPreference
# = 'Stop', that becomes a terminating error - even for routine lifecycle
# output like "Container ... Stopping" that docker compose intentionally
# writes to stderr. Redirecting with 2>&1 does NOT prevent this by itself;
# it just merges the stream, the promotion still happens. Turn the
# promotion off so only actual non-zero exit codes are treated as failures.
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest
$RepoRoot=Resolve-Path (Join-Path $PSScriptRoot '../../..')
$ComposeFile=Join-Path $PSScriptRoot 'docker-compose.yml'; $Project='foundgine-run4'
$DbProject=Join-Path $RepoRoot 'src/csharp/benchmarks/CoffeeBeanery.Performance/CoffeeBeanery.Database/CoffeeBeanery.Database.csproj'
$MetricsScript=Join-Path $RepoRoot 'src/csharp/benchmarks/AgentEndToEnd/scripts/docker-metrics.ps1'
$MetricsSummary=Join-Path $RepoRoot 'src/csharp/benchmarks/AgentEndToEnd/scripts/summarize-docker-metrics.ps1'
$Report=Join-Path $PSScriptRoot 'artifacts'; New-Item -ItemType Directory -Force -Path $Report | Out-Null
if($null -eq $CustomerCounts -or $null -eq $RunsPerTier -or $CustomerCounts.Count -ne $RunsPerTier.Count){throw 'CustomerCounts and RunsPerTier must have the same number of entries.'}
# Any single customer-tier / single-concurrency override (e.g. -Smoke's 1/1,
# or an ad-hoc lightweight run like -CustomerCounts 1 -Concurrency 8) is a
# deliberate one-off and is exempt from the fixed tier matrix below, which
# otherwise governs real (multi-tier) benchmark runs. Previously this only
# exempted the literal 1-customer/concurrency-1 -Smoke shape, so any other
# single-tier override (different customer count or different concurrency)
# incorrectly fell through to the full-matrix assertion below and failed.
$isSingleTierOverride = ($CustomerCounts.Count -eq 1 -and $Concurrency.Count -eq 1)
if (-not $isSingleTierOverride) {
    # This is a required-value check, not a duplication guard: Run 4 is
    # expected to reuse Run 2's tiers for a real (multi-tier) run, and this
    # throws only when a multi-tier run supplies something else.
    if($CustomerCounts.Count -ne 4 -or ($CustomerCounts -join ',') -ne '10,100,1000,10000'){throw 'Run 4 (multi-tier) requires the same customer tiers as Run 2: 10,100,1000,10000.'}
    if(($Concurrency -join ',') -ne '8,16,32,64'){throw 'Run 4 (multi-tier) requires the same concurrency tiers as Run 2: 8,16,32,64.'}
}
$script:PostgresHostPort=$null
$script:ConnectionString=$null
function Get-FreeTcpPort { $listener=[System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback,0); try { $listener.Start(); return $listener.LocalEndpoint.Port } finally { $listener.Stop() } }
function Compose([string[]]$ComposeArgs){ & docker compose -p $Project -f $ComposeFile @ComposeArgs; if($LASTEXITCODE -ne 0){throw "docker compose failed: $($ComposeArgs -join ' ')"} }
function Wait-Tcp([string]$HostName,[int]$Port,[int]$TimeoutSeconds=60){
  # docker compose --wait and the docker-exec pg_isready loop above both
  # check readiness *inside* the container's network namespace. Neither
  # proves the host-published port ($HostName:$Port, the exact endpoint the
  # .NET seeder's Npgsql connection uses) is actually accepting external
  # connections yet - there can be a short lag between "container reports
  # healthy" and the host-side port-forward/NAT accepting traffic. That gap
  # is what produced "Failed to connect to 127.0.0.1:<port> ... TimeoutException"
  # from the seeder even though Postgres was healthy. Probe the real
  # external endpoint directly before handing off to dotnet.
  $deadline=(Get-Date).AddSeconds($TimeoutSeconds)
  while((Get-Date) -lt $deadline){
    try {
      $client=[System.Net.Sockets.TcpClient]::new()
      try {
        $connectTask=$client.ConnectAsync($HostName,$Port)
        if($connectTask.Wait(2000) -and $client.Connected){ return }
      } finally { $client.Dispose() }
    } catch {}
    Start-Sleep -Milliseconds 500
  }
  throw "Postgres host port did not accept connections: ${HostName}:${Port}"
}
function Wait-Http([string]$Url,[int]$TimeoutSeconds=180){
  $deadline=(Get-Date).AddSeconds($TimeoutSeconds)
  while((Get-Date) -lt $deadline){
    try { $r=Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5; if($r.StatusCode -ge 200 -and $r.StatusCode -lt 500){ return } } catch {}
    Start-Sleep -Seconds 2
  }
  Compose @('ps')
  throw "Endpoint did not become ready: $Url"
}
try {
  Compose @('down','-v','--remove-orphans')
  Compose @('build','graphql-ef','mcp-foundgine')
  foreach($index in 0..($CustomerCounts.Count-1)) {
    $customerCount = $CustomerCounts[$index]
    $runsForTier = $RunsPerTier[$index]
    $tier = '{0:D5}-customers' -f $customerCount
    $tierRoot = Join-Path $Report $tier
    New-Item -ItemType Directory -Force -Path $tierRoot | Out-Null
    Write-Host ''
    Write-Host '============================================'
    Write-Host " Run 4 fixture tier: $customerCount customers"
    Write-Host '============================================'
    # Fresh database for every customer tier, exactly like Run 2.
    Compose @('down','-v','--remove-orphans','--timeout','30')
    $script:PostgresHostPort=Get-FreeTcpPort
    $env:POSTGRES_HOST_PORT=$script:PostgresHostPort.ToString()
    $script:ConnectionString="Host=localhost;Port=$($script:PostgresHostPort);Database=foundgine_benchmark;Username=benchmark;Password=benchmark"
    Write-Host "Run 4 PostgreSQL host port: $($script:PostgresHostPort)"
    Compose @('up','-d','--wait','postgres')
    if($LASTEXITCODE -ne 0){
      Compose @('ps'); Compose @('logs','postgres')
      throw "PostgreSQL failed to start for $customerCount customers."
    }
    $dbReady=$false
    for($i=0;$i -lt 60;$i++) {
      $containerId = (& docker compose -p $Project -f $ComposeFile ps -q postgres 2>$null).Trim()
      if($containerId){
        & docker exec $containerId pg_isready -U benchmark -d foundgine_benchmark 2>$null | Out-Null
        if($LASTEXITCODE -eq 0){ $dbReady=$true; break }
      }
      Start-Sleep -Seconds 1
    }
    if(-not $dbReady){ Compose @('ps'); Compose @('logs','postgres'); throw "PostgreSQL did not become ready for $customerCount customers." }
    # The loop above only confirms Postgres is ready *inside* the container.
    # Confirm the actual host-published port the seeder will connect to is
    # live before invoking it, closing the race that produced the Npgsql
    # "Failed to connect to 127.0.0.1:<port>" TimeoutException.
    Wait-Tcp -HostName 'localhost' -Port $script:PostgresHostPort -TimeoutSeconds 60
    # NOTE: CoffeeBeanery.Database's Program.cs resolves the connection string
    # from BankingConnectionString (see Run1\run-agent-benchmark.ps1, which sets
    # this exact variable before invoking the same seeder project). Run4 was
    # previously only setting COFFEEBEANERY_CONNECTION, so on a second
    # invocation within the same PowerShell session the seeder silently fell
    # back to whatever BankingConnectionString was left over from an earlier
    # run/session - a stale host port, even though POSTGRES_HOST_PORT and
    # COFFEEBEANERY_CONNECTION were both correctly refreshed to the new port.
    $env:BankingConnectionString=$script:ConnectionString
    $env:COFFEEBEANERY_CONNECTION=$script:ConnectionString
    $env:COFFEEBEANERY_CUSTOMERS=$customerCount.ToString()
    $env:COFFEEBEANERY_RELATIONSHIPS_PER_CUSTOMER='4'
    $env:COFFEEBEANERY_CONTRACTS_PER_RELATIONSHIP='3'
    $env:COFFEEBEANERY_TRANSACTIONS_PER_CONTRACT='4'
    & dotnet run --project $DbProject --configuration Release
    if($LASTEXITCODE -ne 0){ throw "Fixture seeding failed for $customerCount customers." }
    Compose @('up','-d','graphql-ef','mcp-foundgine')
    if($LASTEXITCODE -ne 0){
      Compose @('ps'); Compose @('logs','graphql-ef'); Compose @('logs','mcp-foundgine')
      throw "Run 4 API services failed to start for $customerCount customers."
    }
    Wait-Http 'http://localhost:4401/health/ready'
    Wait-Http 'http://localhost:4402/health/ready'
    foreach($c in $Concurrency) {
      $runDir = Join-Path $tierRoot ('concurrency-{0:D3}' -f $c)
      New-Item -ItemType Directory -Force -Path $runDir | Out-Null
      $csv=Join-Path $runDir 'docker-metrics.csv'
      $json=Join-Path $runDir 'docker-metrics-summary.json'
      $stop=Join-Path $runDir '.docker-metrics.stop'
      Remove-Item $stop -Force -ErrorAction SilentlyContinue
      $hostCmd=(Get-Command powershell -ErrorAction SilentlyContinue)
      if($null -eq $hostCmd){$hostCmd=(Get-Command pwsh -ErrorAction Stop)}
      $metrics=Start-Process -FilePath $hostCmd.Source -ArgumentList @('-NoProfile','-File',$MetricsScript,'-ComposeFile',$ComposeFile,'-ProjectName',$Project,'-Services','postgres,graphql-ef,mcp-foundgine','-OutputCsv',$csv,'-StopFile',$stop,'-IntervalMs','1000') -PassThru
      try {
        $env:RUN4_MODE=$Mode
        $env:RUN4_RUNS=$runsForTier.ToString()
        $env:RUN4_WARMUPS=$Warmups.ToString()
        $env:RUN4_CUSTOMER_COUNT=$customerCount.ToString()
        $env:RUN4_CONCURRENCY=$c.ToString()
        $env:RUN4_REPORT_DIRECTORY=$runDir
        $env:RUN4_GRAPHQL_URL='http://localhost:4401/graphql'
        $env:RUN4_MCP_URL='http://localhost:4402/mcp'
        Write-Host "Run 4: customers=$customerCount concurrency=$c runs=$runsForTier warmups=$Warmups"
        & dotnet run --project (Join-Path $PSScriptRoot 'Runner/Runner.csproj') -c Release -- $Mode
        if($LASTEXITCODE -ne 0){throw "Run 4 failed for $customerCount customers at concurrency $c (exit code $LASTEXITCODE)"}
      }
      finally {
        New-Item -ItemType File -Force -Path $stop | Out-Null
        try { Wait-Process -Id $metrics.Id -Timeout 10 -ErrorAction SilentlyContinue } catch {}
        if(-not $metrics.HasExited){ try { $metrics.Kill() } catch {} }
        if(Test-Path $csv){ & $hostCmd.Source -NoProfile -File $MetricsSummary -InputCsv $csv -OutputJson $json }
      }
    }
  }
  if ($Publish) {
    $publishScript=Join-Path $PSScriptRoot 'publish-report.ps1'
    & $publishScript -ReportRoot $Report
    if(-not $?){throw 'Benchmark report publication failed.'}
  }
}
finally {
  $previousEap = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $cleanup = @(& docker compose -p $Project -f $ComposeFile down -v --remove-orphans --timeout 30 2>&1)
    $cleanupExit = $LASTEXITCODE
  }
  finally {
    $ErrorActionPreference = $previousEap
  }
  if($cleanupExit -ne 0){ Write-Warning "Benchmark cleanup returned exit code $cleanupExit."; $cleanup | ForEach-Object { Write-Host $_ } }
}