[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InputCsv,
    [Parameter(Mandatory)][string]$OutputJson,
    [string]$ErrorLog = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$rows = @()
if (Test-Path -LiteralPath $InputCsv) { $rows = @(Import-Csv -LiteralPath $InputCsv) }
if ([string]::IsNullOrWhiteSpace($ErrorLog)) { $ErrorLog = "$InputCsv.errors.log" }
$errors = if (Test-Path -LiteralPath $ErrorLog) { @(Get-Content -LiteralPath $ErrorLog) } else { @() }
if ($rows.Count -eq 0) {
    $result = [pscustomobject]@{
        Status = 'no-samples'
        SampleCount = 0
        ErrorCount = $errors.Count
        ErrorLog = $ErrorLog
        Services = @()
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputJson -Encoding utf8
if ($errors.Count -gt 0) {
    Write-Warning "Docker metrics are partial: $($errors.Count) collector errors. Diagnostics: $ErrorLog"
    exit 3
}
    Write-Error "Docker metrics collector produced zero samples. Diagnostics: $ErrorLog"
    exit 2
}
$services = @()
foreach ($group in ($rows | Group-Object Service)) {
    $cpu = @($group.Group | ForEach-Object { [double]$_.CpuPercent })
    $mem = @($group.Group | ForEach-Object { [double]$_.MemoryUsageBytes })
    $netRx = @($group.Group | ForEach-Object { [double]$_.NetRxBytes })
    $netTx = @($group.Group | ForEach-Object { [double]$_.NetTxBytes })
    $blockR = @($group.Group | ForEach-Object { [double]$_.BlockReadBytes })
    $blockW = @($group.Group | ForEach-Object { [double]$_.BlockWriteBytes })
    $ordered = @($group.Group | Sort-Object TimestampUtc)
    $cpuSeconds = 0.0; $memoryByteSeconds = 0.0
    for ($i = 1; $i -lt $ordered.Count; $i++) {
        $dt = ([DateTimeOffset]$ordered[$i].TimestampUtc - [DateTimeOffset]$ordered[$i - 1].TimestampUtc).TotalSeconds
        if ($dt -lt 0) { $dt = 0 }
        $cpuSeconds += ([double]$ordered[$i - 1].CpuPercent / 100.0) * $dt
        $memoryByteSeconds += [double]$ordered[$i - 1].MemoryUsageBytes * $dt
    }
    $services += [pscustomobject]@{
        Service = $group.Name; Samples = $group.Count
        CpuAvgPercent = ($cpu | Measure-Object -Average).Average
        CpuMaxPercent = ($cpu | Measure-Object -Maximum).Maximum
        MemoryAvgBytes = ($mem | Measure-Object -Average).Average
        MemoryMaxBytes = ($mem | Measure-Object -Maximum).Maximum
        NetworkRxLastBytes = ($netRx | Select-Object -Last 1)
        NetworkTxLastBytes = ($netTx | Select-Object -Last 1)
        BlockReadLastBytes = ($blockR | Select-Object -Last 1)
        BlockWriteLastBytes = ($blockW | Select-Object -Last 1)
        CpuSecondsEstimated = $cpuSeconds
        MemoryGBSecondsEstimated = ($memoryByteSeconds / 1GB)
    }
}
$summaryStatus = if ($errors.Count -gt 0) { 'partial' } else { 'samples-collected' }
$result = [pscustomobject]@{
    Status = $summaryStatus
    SampleCount = $rows.Count
    ErrorCount = $errors.Count
    ErrorLog = $ErrorLog
    FirstTimestampUtc = $rows[0].TimestampUtc
    LastTimestampUtc = $rows[-1].TimestampUtc
    Services = $services
}
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputJson -Encoding utf8
if ($errors.Count -gt 0) {
    Write-Warning "Docker metrics are partial: $($errors.Count) collector errors. Diagnostics: $ErrorLog"
    exit 3
}
