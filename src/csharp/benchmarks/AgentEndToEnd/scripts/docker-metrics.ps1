[CmdletBinding()]
param(
[Parameter(Mandatory = $true)]
[string]$ComposeFile,

[string]$ProjectName = '',

[Parameter()]
[string]$Services = 'postgres,foundgine-warm',

[Parameter(Mandatory = $true)]
[string]$OutputCsv,

[Parameter(Mandatory = $true)]
[string]$StopFile,

[int]$IntervalMs = 1000

)

$ErrorActionPreference = 'SilentlyContinue'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------

# Validation

# ---------------------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($ComposeFile)) {
throw 'ComposeFile is required.'
}

if (-not (Test-Path -LiteralPath $ComposeFile)) {
throw "Compose file not found: $ComposeFile"
}

if ([string]::IsNullOrWhiteSpace($OutputCsv)) {
throw 'OutputCsv is required.'
}

if ([string]::IsNullOrWhiteSpace($StopFile)) {
throw 'StopFile is required.'
}

if ($IntervalMs -lt 100) {
$IntervalMs = 100
}

# Normalize the service list. This also protects against callers accidentally

# supplying empty entries.

$Services = @(
($Services -split ',') |
Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
ForEach-Object { $_.Trim() } |
Select-Object -Unique
)

if ($Services.Count -eq 0) {
throw 'At least one Docker Compose service must be supplied.'
}

# ---------------------------------------------------------------------------

# Output setup

# ---------------------------------------------------------------------------

$dir = Split-Path -Parent $OutputCsv

if (-not [string]::IsNullOrWhiteSpace($dir)) {
New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

if (Test-Path -LiteralPath $OutputCsv) {
Remove-Item -LiteralPath $OutputCsv -Force
}

$header = @(
'TimestampUtc',
'Service',
'ContainerId',
'ContainerName',
'CpuPercent',
'MemoryUsageBytes',
'MemoryLimitBytes',
'MemoryPercent',
'NetRxBytes',
'NetTxBytes',
'BlockReadBytes',
'BlockWriteBytes',
'Pids'
) -join ','

Set-Content -LiteralPath $OutputCsv -Value $header -Encoding utf8

# ---------------------------------------------------------------------------

# Helpers

# ---------------------------------------------------------------------------

function Convert-SizeToBytes {
param(
[AllowNull()]
[string]$Value
)

if ([string]::IsNullOrWhiteSpace($Value)) {
    return [double]0
}

$valueNormalized = ($Value.Trim() -replace ',', '')

if ($valueNormalized -match '^([0-9.]+)\s*(B|kB|KB|KiB|MB|MiB|GB|GiB)$') {
    $number = [double]$Matches[1]
    $unit = $Matches[2].ToUpperInvariant()

    switch ($unit) {
        'B'   { return $number }
        'KB'  { return $number * 1000 }
        'KIB' { return $number * 1024 }
        'MB'  { return $number * 1000 * 1000 }
        'MIB' { return $number * 1024 * 1024 }
        'GB'  { return $number * 1000 * 1000 * 1000 }
        'GIB' { return $number * 1024 * 1024 * 1024 }
    }
}

return [double]0

}

function Invoke-ComposePs {
param(
[Parameter(Mandatory = $true)]
[string]$Service
)

if ([string]::IsNullOrWhiteSpace($ProjectName)) {
    & docker compose -f $ComposeFile ps -q $Service 2>$null
}
else {
    & docker compose -p $ProjectName -f $ComposeFile ps -q $Service 2>$null
}

}

function Get-ContainerId {
param(
[Parameter(Mandatory = $true)]
[string]$Service
)

$result = @(Invoke-ComposePs -Service $Service)

foreach ($candidate in $result) {
    if ($null -eq $candidate) {
        continue
    }

    $id = $candidate.ToString().Trim()

    if (-not [string]::IsNullOrWhiteSpace($id)) {
        return $id
    }
}

return ''

}

function Get-DockerStats {
param(
[Parameter(Mandatory = $true)]
[string]$ContainerId
)


$result = & docker stats `
    --no-stream `
    --format '{{.ID}}|{{.Name}}|{{.CPUPerc}}|{{.MemUsage}}|{{.MemPerc}}|{{.NetIO}}|{{.BlockIO}}|{{.PIDs}}' `
    $ContainerId 2>$null

foreach ($line in @($result)) {
    if ($null -eq $line) {
        continue
    }

    $text = $line.ToString().Trim()

    if (-not [string]::IsNullOrWhiteSpace($text)) {
        return $text
    }
}

return ''

}

function Get-MemoryBytes {
param(
[Parameter(Mandatory = $true)]
[string]$Value
)

if ([string]::IsNullOrWhiteSpace($Value)) {
    return [double]0
}

$normalized = $Value.Trim()

if ($normalized -match '^([0-9.]+)\s*(B|kB|KB|KiB|MB|MiB|GB|GiB)$') {
    return Convert-SizeToBytes $normalized
}

return [double]0

}

# ---------------------------------------------------------------------------

# Metrics loop

# ---------------------------------------------------------------------------

while (-not (Test-Path -LiteralPath $StopFile)) {

$timestamp = [DateTimeOffset]::UtcNow.ToString('o')

foreach ($service in $Services) {

    if ([string]::IsNullOrWhiteSpace($service)) {
        continue
    }

    $id = Get-ContainerId -Service $service

    if ([string]::IsNullOrWhiteSpace($id)) {
        continue
    }

    $line = Get-DockerStats -ContainerId $id

    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    $parts = $line -split '\|'

    if ($parts.Count -lt 8) {
        continue
    }

    $cpu = ($parts[2] -replace '%', '').Trim()

    # -------------------------------------------------------------------
    # Memory
    # Docker returns:
    #
    #   12.34MiB / 1.00GiB
    # -------------------------------------------------------------------

    $memoryParts = $parts[3] -split '/'

    if ($memoryParts.Count -ge 2) {
        $memoryUsedText = $memoryParts[0].Trim()
        $memoryLimitText = $memoryParts[1].Trim()

        $memoryUsedBytes = Get-MemoryBytes -Value $memoryUsedText
        $memoryLimitBytes = Get-MemoryBytes -Value $memoryLimitText
    }
    else {
        $memoryUsedBytes = [double]0
        $memoryLimitBytes = [double]0
    }

    $memoryPercent = ($parts[4] -replace '%', '').Trim()

    # -------------------------------------------------------------------
    # Network
    # Docker returns:
    #
    #   1.23MB / 4.56MB
    # -------------------------------------------------------------------

    $networkParts = $parts[5] -split '/'

    if ($networkParts.Count -ge 2) {
        $rx = Convert-SizeToBytes $networkParts[0]
        $tx = Convert-SizeToBytes $networkParts[1]
    }
    else {
        $rx = [double]0
        $tx = [double]0
    }

    # -------------------------------------------------------------------
    # Block IO
    # Docker returns:
    #
    #   1.23MB / 4.56MB
    # -------------------------------------------------------------------

    $blockParts = $parts[6] -split '/'

    if ($blockParts.Count -ge 2) {
        $blockRead = Convert-SizeToBytes $blockParts[0]
        $blockWrite = Convert-SizeToBytes $blockParts[1]
    }
    else {
        $blockRead = [double]0
        $blockWrite = [double]0
    }

    $containerName = $parts[1]
    $pids = $parts[7]

    # -------------------------------------------------------------------
    # CSV escaping
    # -------------------------------------------------------------------

    $fields = @(
        $timestamp
        $service
        $id
        $containerName
        $cpu
        $memoryUsedBytes
        $memoryLimitBytes
        $memoryPercent
        $rx
        $tx
        $blockRead
        $blockWrite
        $pids
    )

    $safeFields = foreach ($field in $fields) {
        $text = if ($null -eq $field) { '' } else { $field.ToString() }

        if ($text.Contains(',') -or
            $text.Contains('"') -or
            $text.Contains("`r") -or
            $text.Contains("`n")) {

            '"' + ($text -replace '"', '""') + '"'
        }
        else {
            $text
        }
    }

    $csvLine = $safeFields -join ','

    Add-Content `
        -LiteralPath $OutputCsv `
        -Value $csvLine `
        -Encoding utf8
}

Start-Sleep -Milliseconds $IntervalMs

}
