param(
    [string[]]$Modes = @('isolated-control', 'live-readonly', 'fault-unhandled-01', 'fault-unhandled-02'),
    [string]$ComponentRoot = 'C:\Users\19851\AppData\Local\Programs\S.T.A.R. Labs\Optical System Design\versions\1.0.4-win-x64-824c7b3e674e',
    [string]$OutputDirectory = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
$probeRoot = $PSScriptRoot
$installedRoot = [IO.Path]::GetFullPath($ComponentRoot)
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$probeDll = Join-Path $probeRoot 'background-probe\bin\Release\net10.0\ClipboardBackgroundProbe.dll'
$baseline = Get-WinEvent -LogName Application -MaxEvents 1
$runs = @()
foreach ($label in $Modes) {
    $mode = if ($label.StartsWith('fault-unhandled')) { 'fault-unhandled' } else { $label }
    $logPath = Join-Path $outputRoot ($label + '.jsonl')
    $arguments = '"' + $probeDll + '" "' + $installedRoot + '" ' + $mode + ' "' + $logPath + '"'
    $started = [DateTimeOffset]::UtcNow
    $probe = Start-Process -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList $arguments -WorkingDirectory $probeRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $outputRoot ($label + '.stdout.log')) -RedirectStandardError (Join-Path $outputRoot ($label + '.stderr.log')) -PassThru
    $timedOut = !$probe.WaitForExit(20000)
    if ($timedOut) { $probe.Kill(); $probe.WaitForExit() }
    $observations = @(Get-Content -LiteralPath $logPath | ForEach-Object { $_ | ConvertFrom-Json })
    $result = [ordered]@{
        Label = $label; Mode = $mode; StartedUtc = $started.ToString('O'); FinishedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        ProcessId = $probe.Id; ExitCode = $probe.ExitCode; TimedOut = $timedOut
        Complete = @($observations | Where-Object Stage -eq 'complete').Count -eq 1
        Unhandled = @($observations | Where-Object Stage -eq 'unhandled' | ForEach-Object Data)
        PasteObservations = @($observations | Where-Object Stage -eq 'paste-observed' | ForEach-Object Data)
        Initialized = @($observations | Where-Object Stage -eq 'initialized' | ForEach-Object Data)
        UndoRedo = @($observations | Where-Object Stage -eq 'undo-redo' | ForEach-Object Data)
    }
    $expectsCrash = $label -eq 'unrelated-com' -or $label.StartsWith('fault-unhandled')
    $result.ExpectedOutcomeMatched = !$timedOut -and $(if ($expectsCrash) {
        $probe.ExitCode -ne 0 -and !$result.Complete -and $result.Unhandled.Count -eq 1 -and $result.Unhandled[0].HResult -eq '8007000E'
    } else { $probe.ExitCode -eq 0 -and $result.Complete -and $result.Unhandled.Count -eq 0 })
    $result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $outputRoot ($label + '.result.json')) -Encoding utf8
    $runs += $result
    Write-Output "$label PID=$($probe.Id) Exit=$($probe.ExitCode) Complete=$($result.Complete) Observed=$($result.PasteObservations.Count)"
    if ($label -eq 'isolated-control' -and (!$result.Complete -or $result.ExitCode -ne 0 -or !$result.PasteObservations[0].MatchesControlledText)) {
        throw 'Positive control failed; do not interpret subsequent diagnostic runs.'
    }
}
$events = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $baseline.TimeCreated; Id = @(1026, 1000) } -ErrorAction SilentlyContinue | Where-Object { $_.RecordId -gt $baseline.RecordId -and $_.Message -match 'ClipboardBackgroundProbe' } | Select-Object TimeCreated,RecordId,Id,ProviderName,Message)
$events | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputRoot 'background-windows-events.json') -Encoding utf8
$summary = [ordered]@{
    Date = '2026-10-26'; InstalledRoot = $installedRoot
    InstalledDllSha256 = (Get-FileHash -LiteralPath (Join-Path $installedRoot 'OpticalSystemDesign.App.dll') -Algorithm SHA256).Hash
    ProductVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $installedRoot 'OpticalSystemDesign.App.dll')).ProductVersion
    NoWindowShowOrActivationCalls = $true; NoKeyboardOrMouseInput = $true; NoGlobalClipboardWrites = $true
    UserClipboardContentsPersisted = $false; BaselineEventRecordId = $baseline.RecordId
    Runs = $runs; WindowsEventCount = $events.Count
    AllExpectedOutcomesMatched = @($runs | Where-Object { !$_.ExpectedOutcomeMatched }).Count -eq 0
    OriginalApplicationProcesses = @(Get-CimInstance Win32_Process -Filter "Name = 'OpticalSystemDesign.App.exe'" | Select-Object ProcessId,ParentProcessId,ExecutablePath)
}
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $outputRoot 'background-summary.json') -Encoding utf8
Write-Output "Windows diagnostic events: $($events.Count)"
if (!$summary.AllExpectedOutcomesMatched) { throw 'One or more background cases did not match their required outcome.' }
