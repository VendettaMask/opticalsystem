param([Parameter(Mandatory)][string]$RepositoryRoot)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($RepositoryRoot)
$evidence=Join-Path $repo 'validation/rechecks/2026-11-04/all-example-accuracy'
$receipt=Get-Content -LiteralPath (Join-Path $evidence 'archive-verification.json') -Raw|ConvertFrom-Json
if(!$receipt.completeReadbackBeforeRetirement){throw 'Accepted evidence has not been fully verified'}
$afocalAttempt=Get-Content -LiteralPath (Join-Path $evidence 'attempts/afocal-missing-runtime.json') -Raw|ConvertFrom-Json
$prepareAttempt=Get-Content -LiteralPath (Join-Path $evidence 'attempts/prepare-path-models.json') -Raw|ConvertFrom-Json
if(!$afocalAttempt.InputsMatchAccepted -or $afocalAttempt.SuccessfulWorkers -ne 0 -or $prepareAttempt.ExecutionFiles -ne 0 -or @($prepareAttempt.Snapshots|Where-Object {!$_.AcceptedMatch}).Count){throw 'Unique attempted work must be preserved'}
$targets=@(
 [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/validation/all-example-accuracy-20261104/study')),
 [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/validation/all-example-afocal-20261104'))
)
$allowed=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts/validation'))+[IO.Path]::DirectorySeparatorChar
foreach($target in $targets){
 if(!$target.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or $target -notin $targets){throw 'Unsafe attempt retirement target'}
 foreach($file in Get-ChildItem -LiteralPath $target -File){Remove-Item -LiteralPath $file.FullName}
 foreach($directory in Get-ChildItem -LiteralPath $target -Directory|Where-Object Name -ne 'runner'){
  $resolved=[IO.Path]::GetFullPath($directory.FullName)
  if(!$resolved.StartsWith($target+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $directory.LinkType){throw 'Unsafe attempt child directory'}
  Remove-Item -LiteralPath $resolved -Recurse
 }
}
Write-Output 'Retired duplicate failed-attempt inputs and outputs; retained unique failure records and the shared active runtime.'
