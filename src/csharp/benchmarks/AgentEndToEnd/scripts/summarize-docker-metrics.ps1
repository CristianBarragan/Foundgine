[CmdletBinding()]
param([Parameter(Mandatory)][string]$InputCsv,[Parameter(Mandatory)][string]$OutputJson)
$rows = @(Import-Csv $InputCsv)
if ($rows.Count -eq 0) { @{ SampleCount=0 } | ConvertTo-Json | Set-Content $OutputJson; exit 0 }
$services = @()
foreach ($g in ($rows | Group-Object Service)) {
    $cpu = @($g.Group | ForEach-Object {[double]$_.CpuPercent})
    $mem = @($g.Group | ForEach-Object {[double]$_.MemoryUsageBytes})
    $netRx = @($g.Group | ForEach-Object {[double]$_.NetRxBytes})
    $netTx = @($g.Group | ForEach-Object {[double]$_.NetTxBytes})
    $blockR = @($g.Group | ForEach-Object {[double]$_.BlockReadBytes})
    $blockW = @($g.Group | ForEach-Object {[double]$_.BlockWriteBytes})
    $ordered = @($g.Group | Sort-Object TimestampUtc)
    $cpuSeconds = 0.0
    $memoryByteSeconds = 0.0
    for ($i = 1; $i -lt $ordered.Count; $i++) {
        $dt = ([DateTimeOffset]$ordered[$i].TimestampUtc - [DateTimeOffset]$ordered[$i-1].TimestampUtc).TotalSeconds
        if ($dt -lt 0) { $dt = 0 }
        $cpuSeconds += ([double]$ordered[$i-1].CpuPercent / 100.0) * $dt
        $memoryByteSeconds += [double]$ordered[$i-1].MemoryUsageBytes * $dt
    }
    $services += [pscustomobject]@{
        Service=$g.Name; Samples=$g.Count
        CpuAvgPercent=($cpu | Measure-Object -Average).Average
        CpuMaxPercent=($cpu | Measure-Object -Maximum).Maximum
        MemoryAvgBytes=($mem | Measure-Object -Average).Average
        MemoryMaxBytes=($mem | Measure-Object -Maximum).Maximum
        NetworkRxLastBytes=($netRx | Select-Object -Last 1)
        NetworkTxLastBytes=($netTx | Select-Object -Last 1)
        BlockReadLastBytes=($blockR | Select-Object -Last 1)
        BlockWriteLastBytes=($blockW | Select-Object -Last 1)
        CpuSecondsEstimated=$cpuSeconds
        MemoryGBSecondsEstimated=($memoryByteSeconds / 1GB)
    }
}
$result = [pscustomobject]@{
    SampleCount=$rows.Count
    FirstTimestampUtc=$rows[0].TimestampUtc
    LastTimestampUtc=$rows[-1].TimestampUtc
    Services=$services
}
$result | ConvertTo-Json -Depth 6 | Set-Content $OutputJson -Encoding utf8
