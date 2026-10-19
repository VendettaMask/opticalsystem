param([Parameter(Mandatory)][string]$RunDirectory)
$ErrorActionPreference='Stop'
$run=(Resolve-Path -LiteralPath $RunDirectory).Path
$repository=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../..')).Path
$tool=Join-Path $PSScriptRoot 'bin/Release/net10.0/SequentialCorpus20261019.dll'
$prefix=$run.TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
$items=@(Get-Content -LiteralPath (Join-Path $run 'items.json') -Raw | ConvertFrom-Json)
$jobs=@($items | Where-Object workbenchJob)
foreach($item in $jobs) {
    $source=[IO.Path]::GetFullPath((Join-Path $item.path 'workbench'))
    $destination=$source+'-before-local-coordinate-repair'
    if(-not $source.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or
        -not $destination.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath (Join-Path $source 'worker-result.json')) -or
        (Test-Path -LiteralPath $destination)) { throw 'Only completed, run-local workbench attempts may be archived once.' }
}
foreach($name in @('analysis-results.json','analysis-summary.json','workbench-results.json')) {
    $path=Join-Path $run $name
    if(Test-Path -LiteralPath $path){Copy-Item -LiteralPath $path -Destination ($path+'.before-local-coordinate-repair')}
}
foreach($item in $jobs) {
    $source=Join-Path $item.path 'workbench'
    $request=[IO.File]::ReadAllBytes((Join-Path $source 'request.json'))
    [IO.Directory]::Move($source,($source+'-before-local-coordinate-repair'))
    [void][IO.Directory]::CreateDirectory($source)
    [IO.File]::WriteAllBytes((Join-Path $source 'request.json'),$request)
}
$core=Join-Path $PSScriptRoot 'bin/Release/net10.0/OpticalSystemDesign.Core.dll'
[ordered]@{reason='Explicit recomputation with final local-coordinate operand repair; prior captures retained';
    jobs=$jobs.Count; preparationIdentitySha256=(Get-FileHash -LiteralPath (Join-Path $run 'identity.json')).Hash.ToLowerInvariant();
    computationCoreSha256=(Get-FileHash -LiteralPath $core).Hash.ToLowerInvariant();
    policySha256=(Get-FileHash -LiteralPath (Join-Path $run 'comparison-settings.json')).Hash.ToLowerInvariant();
    nativeCapturesChanged=$false; snapshotsChanged=$false} | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $run 'workbench-recompute-identity.json') -Encoding utf8
dotnet $tool workbench $repository $run
exit $LASTEXITCODE
