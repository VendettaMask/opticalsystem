param([Parameter(Mandatory)][string]$RunDirectory)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $RunDirectory).Path
$itemsPath=Join-Path $root 'items.json'
$items=Get-Content -LiteralPath $itemsPath -Raw|ConvertFrom-Json -Depth 100
$models=Get-Content -LiteralPath (Join-Path $root 'models.json') -Raw|ConvertFrom-Json
$probe=Get-Content -LiteralPath 'artifacts/validation/afocal-20261024/native-models-v4.json' -Raw|ConvertFrom-Json
$rows=@()
foreach($item in $items|Where-Object {$_.run.key -eq 'MTF' -and $_.run.request}) {
    $request=$item.run.request
    $nativePath=Join-Path $item.path 'native/request.json'
    if(Test-Path -LiteralPath (Join-Path $item.path 'native/data.json')){throw 'Requests must be fixed before capture'}
    $model=$models|Where-Object {$_.id -ceq $item.lens -and $_.configuration -eq $item.configuration}
    $nativeModel=$probe.rows|Where-Object {(Split-Path $_.source -Leaf) -ieq (Split-Path $item.lens -Leaf) -and $_.config -eq $item.configuration}
    if(!$nativeModel -or !$model){throw 'Missing independently captured pupil definition'}
    $minimum=($model.wavelengths.micrometers|Measure-Object -Minimum).Minimum
    $maximum=1.05*[Math]::Abs($nativeModel.exitDiameter)/$minimum
    $request.maximumFrequency=$maximum
    $request.workbenchSettings.MaximumFrequency=$maximum.ToString('R',[Globalization.CultureInfo]::InvariantCulture)
    $job=Get-Content -LiteralPath $item.workbenchJob -Raw|ConvertFrom-Json -Depth 100
    $job.request=$request
    $job|ConvertTo-Json -Depth 100|Set-Content -LiteralPath $item.workbenchJob -Encoding utf8
    $native=Get-Content -LiteralPath $nativePath -Raw|ConvertFrom-Json -Depth 100
    $scale=switch($request.nativeAfocalAngleUnit){'Milliradian'{1.0};'ArcMinute'{[Math]::PI/10.8};'ArcSecond'{[Math]::PI/648.0};default{throw 'Unsupported unit in scoped controls'}}
    $native.maximumFrequency=$maximum*$scale
    $native.workbenchSettings=$request.workbenchSettings
    $native|ConvertTo-Json -Depth 100|Set-Content -LiteralPath $nativePath -Encoding utf8
    $rows+=[ordered]@{lens=$item.lens;configuration=$item.configuration;nativeExitPupilDiameterMillimeters=$nativeModel.exitDiameter;minimumWavelengthMicrometers=$minimum;maximumFrequencyCyclesPerMilliradian=$maximum;multiplier=1.05;nativeMaximumFrequency=$native.maximumFrequency;nativeUnit=$request.nativeAfocalAngleUnit;thresholdsUnchanged=$true;settingsFixedBeforeCapture=$true}
}
if($rows.Count -ne 5){throw 'Five afocal configurations expected'}
$items|ConvertTo-Json -Depth 100|Set-Content -LiteralPath $itemsPath -Encoding utf8
ConvertTo-Json -InputObject $rows -Depth 12|Set-Content -LiteralPath (Join-Path $root 'cutoff-controls.json') -Encoding utf8
