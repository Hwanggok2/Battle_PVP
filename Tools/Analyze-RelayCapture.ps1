param(
    [Parameter(Mandatory = $true)][string]$Directory,
    [double]$WarmupSeconds = 10
)
$ErrorActionPreference = 'Stop'

function Get-Distribution([double[]]$Values) {
    $ordered = @($Values | Sort-Object)
    if ($ordered.Count -eq 0) { return @{ samples = 0; p50 = $null; p95 = $null; p99 = $null; max = $null } }
    return @{
        samples = $ordered.Count
        p50 = $ordered[[math]::Ceiling($ordered.Count * .50) - 1]
        p95 = $ordered[[math]::Ceiling($ordered.Count * .95) - 1]
        p99 = $ordered[[math]::Ceiling($ordered.Count * .99) - 1]
        max = $ordered[-1]
    }
}

$results = @(foreach ($pongFile in Get-ChildItem -LiteralPath $Directory -Filter '*-pongs.csv') {
    $stem = $pongFile.FullName.Substring(0, $pongFile.FullName.Length - '-pongs.csv'.Length)
    $metadata = Get-Content -LiteralPath ($stem + '.json') -Raw | ConvertFrom-Json
    $frames = @(Import-Csv -LiteralPath ($stem + '-frames.csv') | Where-Object {
        [double]$_.seconds -ge $WarmupSeconds -and [int]$_.peers -gt 0
    })
    $pongs = @(Import-Csv -LiteralPath $pongFile.FullName | Where-Object { [double]$_.seconds -ge $WarmupSeconds })
    $summary = [ordered]@{
        capture = [IO.Path]::GetFileName($stem)
        role = $metadata.role; region = $metadata.region; protocol = $metadata.protocol; route = $metadata.route
        completed = $metadata.reason; warmupExcludedSeconds = $WarmupSeconds
        rawRttMs = Get-Distribution @($pongs | ForEach-Object { [double]$_.rawRttMs })
        frameMs = Get-Distribution @($frames | ForEach-Object { [double]$_.frameMs })
        queueFullDelta = $null; unreliableDropDelta = $null; sendErrorDelta = $null
        maxBacklogBytes = $null; maxBacklogSeconds = $null; gc0Delta = $null
    }
    if ($frames.Count -gt 1) {
        $first = $frames[0]; $last = $frames[-1]
        $summary.queueFullDelta = [long]$last.queueFull - [long]$first.queueFull
        $summary.unreliableDropDelta = [long]$last.unreliableDrops - [long]$first.unreliableDrops
        $summary.sendErrorDelta = [long]$last.sendErrors - [long]$first.sendErrors
        $summary.maxBacklogBytes = ($frames | Measure-Object backlogBytes -Maximum).Maximum
        $summary.maxBacklogSeconds = ($frames | Measure-Object backlogAgeSeconds -Maximum).Maximum
        $summary.gc0Delta = [int]$last.gc0 - [int]$first.gc0
    }
    [pscustomobject]$summary
})
if ($results.Count -eq 0) { throw 'No completed capture files were found.' }
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Directory 'analysis.json') -Encoding utf8
$results | ConvertTo-Json -Depth 5
