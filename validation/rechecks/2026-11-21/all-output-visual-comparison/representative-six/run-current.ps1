param([int]$Lane=0)
$ErrorActionPreference='Stop'
$captureRoot=$PSScriptRoot
$captureInputs=Get-Content -LiteralPath (Join-Path $captureRoot 'inputs.json') -Raw | ConvertFrom-Json
$captureTool=Join-Path (Split-Path $captureRoot) 'capture/bin/Release/net10.0/VisualCapture.dll'
$env:OPTICAL_VISUAL_CAPTURE_GRAPHICS_ONLY='1'
for($captureIndex=$Lane;$captureIndex -lt $captureInputs.lenses.Count;$captureIndex+=2){
  $captureLens=$captureInputs.lenses[$captureIndex]
  $captureOutput=Join-Path $captureRoot "$($captureLens.name)/workbench"
  for($captureAttempt=0;$captureAttempt -lt 80;$captureAttempt++){
    & dotnet $captureTool $captureLens.source $captureOutput
    $captureManifestPath=Join-Path $captureOutput 'manifest.json'
    if(Test-Path -LiteralPath $captureManifestPath){
      $captureManifest=Get-Content -LiteralPath $captureManifestPath -Raw | ConvertFrom-Json
      if($captureManifest.Results.Count -ge 71){break}
    } elseif(Test-Path -LiteralPath (Join-Path $captureOutput 'error.txt')){break}
  }
  $captureFinal=Get-Content -LiteralPath $captureManifestPath -Raw | ConvertFrom-Json
  if($captureFinal.Results.Count -lt 71){throw "Incomplete graphical inventory for $($captureLens.name): $($captureFinal.Results.Count) / 71"}
  "FINISHED $($captureLens.name)"
}
