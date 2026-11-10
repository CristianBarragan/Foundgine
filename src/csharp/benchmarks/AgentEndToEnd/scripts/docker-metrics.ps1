[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ComposeFile,
    [string]$ProjectName = '',
    [string]$Services = 'postgres,foundgine-warm',
    [Parameter(Mandatory = $true)][string]$OutputCsv,
    [Parameter(Mandatory = $true)][string]$StopFile,
    [string]$ErrorLog = '',
    [int]$IntervalMs = 1000
)

# Metrics are diagnostic evidence. Never hide collector failures: persist them
# separately from the benchmark process so a failed sampler cannot look like
# a successful run with zero resource usage.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($ComposeFile) -or -not (Test-Path -LiteralPath $ComposeFile)) {
    throw "Compose file not found: $ComposeFile"
}
if ([string]::IsNullOrWhiteSpace($OutputCsv)) { throw 'OutputCsv is required.' }
if ([string]::IsNullOrWhiteSpace($StopFile)) { throw 'StopFile is required.' }
if ($IntervalMs -lt 100) { $IntervalMs = 100 }
if ([string]::IsNullOrWhiteSpace($ErrorLog)) { $ErrorLog = "$OutputCsv.errors.log" }

$servicesToSample = @($Services -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique)
if ($servicesToSample.Count -eq 0) { throw 'At least one Docker Compose service must be supplied.' }

foreach ($path in @($OutputCsv, $ErrorLog)) {
    $parent = Split-Path -Parent $path
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
}
Remove-Item -LiteralPath $OutputCsv -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $ErrorLog -Force -ErrorAction SilentlyContinue
$header = 'TimestampUtc,Service,ContainerId,ContainerName,CpuPercent,MemoryUsageBytes,MemoryLimitBytes,MemoryPercent,NetRxBytes,NetTxBytes,BlockReadBytes,BlockWriteBytes,Pids'
Set-Content -LiteralPath $OutputCsv -Value $header -Encoding utf8

$script:ErrorCount = 0
function Write-CollectorError([string]$Message) {
    $script:ErrorCount++
    $line = "$( [DateTimeOffset]::UtcNow.ToString('o') ) $Message"
    Add-Content -LiteralPath $ErrorLog -Value $line -Encoding utf8
}

function Invoke-Docker([string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& docker @Arguments 2>&1)
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    [pscustomobject]@{ ExitCode = $code; Output = $output }
}

function Get-ContainerId([string]$Service) {
    $args = @('compose')
    if ($ProjectName) { $args += @('-p', $ProjectName) }
    $args += @('-f', $ComposeFile, 'ps', '-q', $Service)
    $result = Invoke-Docker -Arguments $args
    if ($result.ExitCode -ne 0) {
        Write-CollectorError "docker compose ps failed for service '$Service' (exit $($result.ExitCode)): $($result.Output -join ' ')"
        return ''
    }
    foreach ($item in $result.Output) {
        $candidate = ([string]$item).Trim()
        if ($candidate -match '^[0-9a-f]{12,64}$') { return $candidate }
    }
    return ''
}

function Convert-SizeToBytes([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return [double]0 }
    if ($Value.Trim() -match '^([0-9.]+)\s*(B|kB|KB|KiB|MB|MiB|GB|GiB)$') {
        $number = [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
        switch ($Matches[2].ToUpperInvariant()) {
            'B' { return $number }
            'KB' { return $number * 1000 }
            'KIB' { return $number * 1024 }
            'MB' { return $number * 1000000 }
            'MIB' { return $number * 1048576 }
            'GB' { return $number * 1000000000 }
            'GIB' { return $number * 1073741824 }
        }
    }
    Write-CollectorError "Unrecognized Docker size value '$Value'."
    return [double]0
}

function Get-DockerStats([string]$ContainerId) {
    $result = Invoke-Docker -Arguments @('stats', '--no-stream', '--format', '{{.ID}}|{{.Name}}|{{.CPUPerc}}|{{.MemUsage}}|{{.MemPerc}}|{{.NetIO}}|{{.BlockIO}}|{{.PIDs}}', $ContainerId)
    if ($result.ExitCode -ne 0) {
        Write-CollectorError "docker stats failed for container '$ContainerId' (exit $($result.ExitCode)): $($result.Output -join ' ')"
        return ''
    }
    foreach ($item in $result.Output) {
        $line = ([string]$item).Trim()
        if ($line) { return $line }
    }
    Write-CollectorError "docker stats returned no row for container '$ContainerId'."
    return ''
}

$missingReported = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
while (-not (Test-Path -LiteralPath $StopFile)) {
    $timestamp = [DateTimeOffset]::UtcNow.ToString('o')
    foreach ($service in $servicesToSample) {
        $id = Get-ContainerId -Service $service
        if (-not $id) {
            if ($missingReported.Add($service)) { Write-CollectorError "No running container found for compose service '$service'." }
            continue
        }
        $line = Get-DockerStats -ContainerId $id
        if (-not $line) { continue }
        $parts = $line -split '\|', 8
        if ($parts.Count -ne 8) { Write-CollectorError "Malformed docker stats row for '$service': $line"; continue }

        $memory = $parts[3] -split '/', 2
        $network = $parts[5] -split '/', 2
        $block = $parts[6] -split '/', 2
        if ($memory.Count -ne 2 -or $network.Count -ne 2 -or $block.Count -ne 2) {
            Write-CollectorError "Malformed docker stats units for '$service': $line"
            continue
        }
        $cpu = ($parts[2] -replace '%', '').Trim()
        $memPercent = ($parts[4] -replace '%', '').Trim()
        $fields = @(
            $timestamp, $service, $id, $parts[1], $cpu,
            (Convert-SizeToBytes $memory[0]), (Convert-SizeToBytes $memory[1]), $memPercent,
            (Convert-SizeToBytes $network[0]), (Convert-SizeToBytes $network[1]),
            (Convert-SizeToBytes $block[0]), (Convert-SizeToBytes $block[1]), $parts[7]
        )
        $safe = foreach ($field in $fields) {
            $text = if ($null -eq $field) { '' } else { [string]$field }
            if ($text.Contains(',') -or $text.Contains('"') -or $text.Contains("`r") -or $text.Contains("`n")) {
                '"' + ($text -replace '"', '""') + '"'
            } else { $text }
        }
        Add-Content -LiteralPath $OutputCsv -Value ($safe -join ',') -Encoding utf8
    }
    Start-Sleep -Milliseconds $IntervalMs
}

$rows = 0
if (Test-Path -LiteralPath $OutputCsv) {
    $lineCount = @(Get-Content -LiteralPath $OutputCsv).Count
    $rows = [Math]::Max(0, $lineCount - 1)
}
$status = if ($rows -eq 0) { 'no-samples' } elseif ($script:ErrorCount -gt 0) { 'partial' } else { 'samples-collected' }
@{ Status = $status; SampleCount = $rows; ErrorCount = $script:ErrorCount; ErrorLog = $ErrorLog } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$OutputCsv.collector.json" -Encoding utf8
if ($rows -eq 0) { exit 2 }
if ($script:ErrorCount -gt 0) { exit 3 }
