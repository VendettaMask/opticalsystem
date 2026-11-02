param([string]$Root = (Resolve-Path "$PSScriptRoot/../../..").Path)
$ErrorActionPreference = 'Stop'
$source = Join-Path $Root 'local-data/references/ansys-scene-sfr-2025r1/Data_MTF_KB.zip'
$output = Join-Path $Root 'validation/rechecks/2026-11-02/ansys-scene-sfr'
[IO.Directory]::CreateDirectory($output) | Out-Null
$inputStream = [IO.File]::OpenRead($source)
try { $sha = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($inputStream)) } finally { $inputStream.Dispose() }
$zip = [IO.Compression.ZipFile]::OpenRead($source)
try {
    $entries = foreach ($entry in $zip.Entries | Where-Object { $_.Name -ne '' }) {
        $stream = $entry.Open()
        try { $hash = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
        [ordered]@{ Name=$entry.FullName; Bytes=$entry.Length; Sha256=$hash }
    }
    $spectra = foreach ($entry in $zip.Entries | Where-Object { $_.Name.EndsWith('.spectrum') }) {
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $lines = $reader.ReadToEnd() -split '[\r\n]+' | Where-Object { $_ -ne '' } } finally { $reader.Dispose() }
        [ordered]@{ Name=$entry.FullName; Header=$lines[0]; Description=$lines[1]; Samples=@($lines | Select-Object -Skip 3) }
    }
    $mem = [IO.MemoryStream]::new(); $stream = $zip.GetEntry('Data - MTF KB/MTF_workflow_end.scdocx').Open()
    try { $stream.CopyTo($mem) } finally { $stream.Dispose() }; $mem.Position = 0
    $project = [IO.Compression.ZipArchive]::new($mem, [IO.Compression.ZipArchiveMode]::Read)
    try {
        $reader = [IO.StreamReader]::new($project.GetEntry('SpaceClaim/document.xml').Open())
        try { $decoded = [Net.WebUtility]::HtmlDecode($reader.ReadToEnd()) } finally { $reader.Dispose() }
        $idx = $decoded.IndexOf('<key>_HorzPixel</key>'); if ($idx -lt 3000) { throw 'Camera dictionary not found' }
        $slice = $decoded.Substring($idx - 3000, 6000)
        $camera = [ordered]@{}
        foreach ($match in [regex]::Matches($slice, '<item><key>([^<]+)</key><value[^>]*>([^<]{0,200})</value>')) {
            $camera[$match.Groups[1].Value] = $match.Groups[2].Value
        }
        $all = [regex]::Matches($decoded, '<item><key>(_DurationLimit|_DurationLimited|_Flux|_FluxType|_StopCriteria|_Power|_Luminance|_LuminanceUnit|_Unit|_SurfaceFlux)</key><value[^>]*>([^<]{0,200})</value>') |
            ForEach-Object { [ordered]@{ Key=$_.Groups[1].Value; Value=$_.Groups[2].Value } }
        $reader = [IO.StreamReader]::new($project.GetEntry('docProps/custom.xml').Open())
        try { $custom = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
        $version = $custom.SelectSingleNode('//*[@name="Ansys.Speos.DevelopmentVersion"]/*').InnerText
    } finally { $project.Dispose(); $mem.Dispose() }
    $reader = [IO.StreamReader]::new($zip.GetEntry('Data - MTF KB/ZOS/Lens 710.ZMX').Open())
    try { $lensLines = $reader.ReadToEnd() -split '[\r\n]+' | ForEach-Object { $_.Trim() } } finally { $reader.Dispose() }
    $lensSettings = @($lensLines | Where-Object { $_ -match '^(VERS|FTYP|PWAV|ENPD|FNUM|OBNA|DISZ|GLAS|YFLN|XFLN)\s' -or $_ -match '^WAVM [123] ' })
    $stream = $zip.GetEntry('Data - MTF KB/ZOS/Lens 710.OPTDistortion').Open(); $header = [byte[]]::new(32)
    try { $count=$stream.Read($header,0,32) } finally { $stream.Dispose() }
    $record = [ordered]@{
        SourceUrl='https://zendeskuploads.lumerical.com/guide_articles/38443588650899/Data_MTF_KB.zip'
        ArticleUrl='https://optics.ansys.com/hc/en-us/articles/38443588650899-Camera-MTF-calculation-using-the-slanted-edge-method-in-Ansys-Speos'
        ReferenceScope='Ansys Speos 2025 R1 original downloadable package; local reference only, not bundled with product'
        ArchiveRelativePath='local-data/references/ansys-scene-sfr-2025r1/Data_MTF_KB.zip'
        ArchiveBytes=(Get-Item -LiteralPath $source).Length; ArchiveSha256=$sha; Entries=@($entries)
        SpeosProjectVersion=$version; CameraDictionaryExcerpt=$camera; SimulationAndSourceScalarOccurrences=@($all)
        LensSettings=$lensSettings; Spectra=@($spectra); RomHeaderHex=[Convert]::ToHexString($header,0,$count)
        RomStatus='Binary payload. Version/semantics not decoded; unsupported. No inferred/guessed optical model.'
        PixelPitchMicrometersFromDimensions=0.5; FullWidthPixels=5000; FullHeightPixels=3000
        FullFramePixels=15000000; CurrentProducerPixelLimit=262144; CurrentRayLimit=8388608
        FullFrameMinimumRaysPerBand=15000000
        BudgetStatus='Original full resolution exceeds existing pixel and ray limits even at one ray/pixel. Not rendered; no lowering resolution and claiming original parity.'
        Pending=@('Exact chart geometry/pose and source radiometric units from serialized source/geometry', 'ROM version and supported interface', 'Independent MTF Mapper version/flags and numeric curves', 'Formal result tolerances and full-frame budget design')
    }
    $record | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'original-reference-inventory.json') -Encoding utf8
    [pscustomobject]@{ArchiveSha256=$sha; Files=@($entries).Count; SpeosVersion=$version; Camera='5000 x 3000; 2.5 x 1.5 mm'; Rom='binary / unsupported'} | ConvertTo-Json
} finally { $zip.Dispose() }
