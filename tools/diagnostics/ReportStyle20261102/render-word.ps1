param([string]$root = 'D:/optical system design/opticalsystem/output/report-style-gauss28-20261102')
$ErrorActionPreference = 'Stop'
$qa = Join-Path $root 'qa'
$copy = Join-Path $qa 'word-source.docx'
Copy-Item -LiteralPath (Join-Path $root 'Reports/Double Gauss 28 degree field_full_report.docx') -Destination $copy -Force
$before = @(Get-Process WINWORD -ErrorAction SilentlyContinue | ForEach-Object Id)
$word = $null
$owned = $false
$document = $null
try {
    $word = New-Object -ComObject Word.Application
    $created = @(Get-Process WINWORD -ErrorAction SilentlyContinue | Where-Object { $before -notcontains $_.Id })
    if ($created.Count -ne 1 -or $word.Documents.Count -ne 0) { throw 'Refusing to use a pre-existing Word instance.' }
    $owned = $true
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $word.AutomationSecurity = 3
    $document = $word.Documents.Open($copy, $false, $true, $false)
    [void]$document.Fields.Update()
    $document.Repaginate()
    $pdf = Join-Path $qa 'word-rendered.pdf'
    $document.ExportAsFixedFormat($pdf, 17)
    $info = @{ WordVersion = $word.Version; Pages = $document.ComputeStatistics(2); NativeEquations = $document.OMaths.Count; Pdf = $pdf; ForegroundInteraction = $false }
    $info | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $qa 'word-render.json') -Encoding utf8
    $info | ConvertTo-Json -Compress
}
finally {
    if ($owned) {
        if ($document) { $document.Close(0); [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($document) }
        $word.Quit(0)
    }
    if ($word) { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) }
}
