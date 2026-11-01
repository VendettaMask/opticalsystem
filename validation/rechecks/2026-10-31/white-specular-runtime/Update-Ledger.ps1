$ErrorActionPreference='Stop'
$rows=@(foreach($directory in Get-ChildItem -LiteralPath $PSScriptRoot -Directory){
    $file=Join-Path $directory.FullName 'simulation.html'
    if(!(Test-Path -LiteralPath $file)){continue}
    $text=[IO.File]::ReadAllText($file)
    $powers=@([regex]::Matches($text,'<td>([0-9.eE+-]+) Watt</td>')|ForEach-Object {[double]::Parse($_.Groups[1].Value,[Globalization.CultureInfo]::InvariantCulture)})
    $meta=Get-Content (Join-Path $directory.FullName 'native-interactive-trace.json') -Raw|ConvertFrom-Json
    $count=[regex]::Match($text,'Number of emitted rays</th>\s*<td>(\d+)</td>')
    if(!$count.Success -or $powers.Count -ne 6){throw "Unexpected native HTML in $($directory.Name)"}
    [pscustomobject]@{Case=$directory.Name;WavelengthNm=$meta.WavelengthNm;SourceSha256=$meta.SourceSha256;RequestedRays=$meta.RequestedRays;ActualRays=[long]$count.Groups[1].Value
        TargetIncidenceDegrees=if($null -ne $meta.TargetIncidenceDegrees){$meta.TargetIncidenceDegrees}else{0.0}
        TargetNormal=if($null -ne $meta.TargetNormal){$meta.TargetNormal}else{@(0.0,0.0,1.0)}
        AbsorbedWatts=$powers[0];ErrorWatts=$powers[1];SourceWatts=$powers[2];GeneratedWatts=$powers[3];TruncatedWatts=$powers[4];EscapedWatts=$powers[5]
        ClosureError=[Math]::Abs($powers[2]-$powers[0]-$powers[1]-$powers[4]-$powers[5])
        HtmlSha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLower()
        MapSha256=(Get-FileHash -LiteralPath (Join-Path $directory.FullName 'native-direct-result.xmp') -Algorithm SHA256).Hash.ToLower()}
})
$rows|ConvertTo-Json -Depth 7|Set-Content (Join-Path $PSScriptRoot 'native-power-ledger.json')
$rows|Measure-Object ActualRays -Minimum -Maximum -Sum
