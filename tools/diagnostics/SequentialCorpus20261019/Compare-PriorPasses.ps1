param([Parameter(Mandatory)][string]$BaselineRun,
      [Parameter(Mandatory)][string]$CurrentRun,
      [Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference='Stop'
function Read-Document([string]$Path) { [System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText((Resolve-Path -LiteralPath $Path))) }
function Canonical-Json([System.Text.Json.JsonElement]$Value) {
    switch ($Value.ValueKind.ToString()) {
        'Object' {
            $properties=@($Value.EnumerateObject() | Sort-Object -CaseSensitive Name | ForEach-Object {
                [System.Text.Json.JsonSerializer]::Serialize([object][string]$_.Name,[type][string],[System.Text.Json.JsonSerializerOptions]$null)+':'+(Canonical-Json $_.Value)
            })
            return '{'+($properties -join ',')+'}'
        }
        'Array' { return '['+(@($Value.EnumerateArray() | ForEach-Object { Canonical-Json $_ }) -join ',')+']' }
        'Number' { return $Value.GetDouble().ToString('R',[Globalization.CultureInfo]::InvariantCulture) }
        'String' { return [System.Text.Json.JsonSerializer]::Serialize([object][string]$Value.GetString(),[type][string],[System.Text.Json.JsonSerializerOptions]$null) }
        default { return $Value.GetRawText() }
    }
}
function Key([System.Text.Json.JsonElement]$Item) {
    $run=$Item.GetProperty('run'); $request=$run.GetProperty('request')
    if($request.ValueKind.ToString() -eq 'Null'){return $null}
    return (@($Item.GetProperty('lens').GetString(),$Item.GetProperty('configuration').GetInt32(),$Item.GetProperty('scope').GetString(),$run.GetProperty('key').GetString(),$request.GetProperty('field').GetInt32(),$request.GetProperty('wavelength').GetInt32(),$request.GetProperty('pupilSampling').GetInt32()) -join '|')
}
$baseline=Read-Document (Join-Path $BaselineRun 'analysis-results.json')
$current=Read-Document (Join-Path $CurrentRun 'analysis-results.json')
try {
    $lookup=[Collections.Generic.Dictionary[string,System.Text.Json.JsonElement]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($item in $current.RootElement.EnumerateArray()){
        $key=Key $item
        if($key){if($lookup.ContainsKey($key)){throw "Ambiguous current request key: $key"};$lookup.Add($key,$item)}
    }
    $rows=@(foreach($old in $baseline.RootElement.EnumerateArray()){
        if($old.GetProperty('run').GetProperty('conclusion').GetString() -cne 'Pass'){continue}
        $key=Key $old
        if(!$key -or !$lookup.ContainsKey($key)){[pscustomobject][ordered]@{key=$key;status='MissingCurrentRequest';currentConclusion=$null;settingsEqual=$false};continue}
        $new=$lookup[$key]
        $equal=(Canonical-Json $old.GetProperty('run').GetProperty('request')) -ceq (Canonical-Json $new.GetProperty('run').GetProperty('request'))
        $conclusion=$new.GetProperty('run').GetProperty('conclusion').GetString()
        [pscustomobject][ordered]@{key=$key;status=$(if($conclusion -ceq 'Pass'){'StillPass'}elseif($equal){'PreviouslyPassNowNotPass'}else{'SettingsChangedRequiresReview'});currentConclusion=$conclusion;settingsEqual=$equal}
    })
    $policyBefore=(Get-FileHash -LiteralPath (Join-Path $BaselineRun 'comparison-settings.json')).Hash.ToLowerInvariant()
    $policyAfter=(Get-FileHash -LiteralPath (Join-Path $CurrentRun 'comparison-settings.json')).Hash.ToLowerInvariant()
    if($policyBefore -cne $policyAfter){throw 'Comparison policy changed'}
    [ordered]@{baselineRun=(Resolve-Path $BaselineRun).Path;currentRun=(Resolve-Path $CurrentRun).Path;policySha256=$policyAfter;baselinePasses=$rows.Count;counts=@($rows|Group-Object status|Select-Object Name,Count);scope='Request-key preservation audit; exact complete canonical settings equality reported separately. Raw numerical captures are not modified.';rows=$rows}|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
    $rows|Group-Object status|Select-Object Name,Count|ConvertTo-Json
} finally {$baseline.Dispose();$current.Dispose()}
