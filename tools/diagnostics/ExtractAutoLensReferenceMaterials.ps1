param([Parameter(Mandatory=$true)][string]$Source, [Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$entries = @{}
$sources = @()
foreach ($catalog in 'MISC','PLASTIC2022','CDGM','SCHOTT') {
    $path = Join-Path $Source ('deeplens/material/' + $catalog + '.AGF')
    $sources += [ordered]@{ File=('deeplens/material/' + $catalog + '.AGF'); Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    $current = $null
    foreach ($line in ((Get-Content -Raw -LiteralPath $path) -split '\r?\n')) {
        $tokens = $line.Trim() -split '\s+'
        if ($tokens[0] -eq 'NM') {
            $current = [ordered]@{ Name=$tokens[1].ToLowerInvariant(); Catalog=$catalog; Formula=[int]$tokens[2]; Index=[double]$tokens[4]; Abbe=[double]$tokens[5]; MinimumNm=0.0; MaximumNm=0.0; Coefficients=@(); MatchIndex=0.0; MatchAbbe=0.0 }
            $entries[$current.Name] = $current
        } elseif ($null -ne $current -and $tokens[0] -eq 'CD') {
            $current.Coefficients = @(1..6 | ForEach-Object { [double]$tokens[$_] })
        } elseif ($null -ne $current -and $tokens[0] -eq 'LD') {
            $current.MinimumNm = 1000 * [double]$tokens[1]
            $current.MaximumNm = 1000 * [double]$tokens[2]
        }
    }
}
$customPath = Join-Path $Source 'deeplens/material/materials_data.json'
$custom = Get-Content -Raw -LiteralPath $customPath | ConvertFrom-Json
$sources += [ordered]@{ File='deeplens/material/materials_data.json'; Sha256=(Get-FileHash -LiteralPath $customPath -Algorithm SHA256).Hash.ToLowerInvariant() }
$initial = @('n-bk7','n-sk16','h-k9l','n-lak14','n-sk2','bk7','n-lak7','f2','n-f2','n-sf5','n-sf11','n-sf1','pmma','coc','okp4') | Where-Object { $entries.ContainsKey($_) }
$projection = @($custom.CDGM_GLASS.PSObject.Properties | ForEach-Object {
    $name = $_.Name.ToLowerInvariant()
    if (!$entries.ContainsKey($name)) { throw "Missing original dispersion: $name" }
    $entries[$name].MatchIndex = [double]$_.Value[0]
    $entries[$name].MatchAbbe = [double]$_.Value[1]
    $name
})
$selected = @($initial + $projection | Select-Object -Unique | ForEach-Object {
    $entry = $entries[$_]
    if ($entry.Formula -notin 1,2 -or $entry.Coefficients.Count -ne 6 -or $entry.MinimumNm -le 0 -or $entry.MaximumNm -lt 656) { throw "Unsupported source entry: $_" }
    $entry
})
$asset = [ordered]@{ Commit='e58db32f3a0b07f25861b5839a45970f77b5c947'; Source='https://github.com/AI4Optics/AutoLens'; License='Apache-2.0 (upstream redistribution; no manufacturer certification)'; Sources=$sources; InitialNames=@($initial); ProjectionNames=$projection; Entries=$selected }
$asset | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Destination -Encoding utf8NoBOM
