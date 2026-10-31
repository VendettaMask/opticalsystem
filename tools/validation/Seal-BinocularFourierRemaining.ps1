param(
    [ValidateSet('Reference','Evidence')][string]$Mode,
    [string]$RepositoryRoot = (Resolve-Path "$PSScriptRoot/../..").Path,
    [string]$StudyRoot = 'artifacts/validation/binocular-fourier-remaining-20261031',
    [string]$TestDirectory = 'artifacts/validation/binocular-fourier-remaining-tests-20261031'
)
$ErrorActionPreference='Stop'
$study = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $StudyRoot))
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Json($Value,[string]$Path) { $Value|ConvertTo-Json -Depth 80|Set-Content -LiteralPath $Path -Encoding utf8 }
function Copy-File([string]$Source,[string]$Target) {
    New-Item -ItemType Directory -Path (Split-Path $Target) -Force|Out-Null
    Copy-Item -LiteralPath $Source -Destination $Target
}
function Seal([string]$Stage,[string]$Destination,[string]$ArchiveName) {
    New-Item -ItemType Directory -Path $Destination -Force|Out-Null
    $archive=Join-Path $Destination $ArchiveName
    if(Test-Path -LiteralPath $archive){throw "Immutable archive already exists: $archive"}
    [IO.Compression.ZipFile]::CreateFromDirectory($Stage,$archive,[IO.Compression.CompressionLevel]::Optimal,$false)
    $entries=@();$zip=[IO.Compression.ZipFile]::OpenRead($archive)
    try { foreach($entry in $zip.Entries|Sort-Object FullName){
        $stream=$entry.Open();try{$sha=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
        $entries+=@{path=$entry.FullName;sha256=$sha;bytes=$entry.Length}
    }}finally{$zip.Dispose()}
    $manifest=@{sha256=(Hash $archive);bytes=(Get-Item -LiteralPath $archive).Length;entries=$entries}
    Json $manifest (Join-Path $Destination 'manifest.json')
    $verified=@();$zip=[IO.Compression.ZipFile]::OpenRead($archive)
    try {foreach($record in $entries){$entry=$zip.GetEntry($record.path);$stream=$entry.Open()
        try{$sha=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
        if($sha -ne $record.sha256 -or $entry.Length -ne $record.bytes){throw "Archive verification failed: $($record.path)"}
        $verified+=@{path=$record.path;sha256=$sha;bytes=$entry.Length;matched=$true}
    }}finally{$zip.Dispose()}
    Json @{archiveSha256=(Hash $archive);verifiedEntries=$verified.Count;allMatched=$true;entries=$verified} (Join-Path $Destination 'verification.json')
    Write-Output "$ArchiveName : $($manifest.sha256); $($entries.Count) entries; $($manifest.bytes) bytes"
}
$policy=Join-Path $RepositoryRoot 'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json'
$cases=Get-Content -LiteralPath "$study/cases.json" -Raw|ConvertFrom-Json
if($Mode -eq 'Reference') {
    $stage="$study/reference-stage"
    if(Test-Path -LiteralPath $stage){throw 'Fresh reference stage required'}
    New-Item -ItemType Directory $stage|Out-Null
    $inputs=@();foreach($config in 1..2){
        $case=$cases|Where-Object configuration -eq $config|Select-Object -First 1
        if((Hash $case.source) -ne $case.sourceSha256 -or (Hash $case.snapshot) -ne $case.snapshotSha256){throw 'Original input changed'}
        $snapshot="inputs/c$config.optic.json"
        Copy-File $case.snapshot "$stage/$snapshot"
        $inputs+=@{configuration=$config;snapshot=$snapshot;snapshotSha256=$case.snapshotSha256;sourceSha256=$case.sourceSha256}
    }
    Copy-File $cases[0].source "$stage/inputs/Binocular_System.ZMX"
    foreach($name in @('native-display-proof.json','native-display-natural-control.json')){Copy-File "$study/$name" "$stage/$name"}
    foreach($config in 1..2){foreach($sampling in @(128,256)){
        $from="$study/derived-curves/c$config-n$sampling"
        foreach($file in Get-ChildItem -LiteralPath $from -File){Copy-File $file.FullName "$stage/curves/c$config-n$sampling/$($file.Name)"}
    }}
    $prior=Join-Path $RepositoryRoot 'artifacts/validation/afocal-mtf-field-20261031'
    $cause=Join-Path $RepositoryRoot 'artifacts/validation/binocular-fourier-cause-20261031'
    $checks=@()
    foreach($spec in @(@{c=1;n=128;nodes="$study/node-control/nodes";curve="$cause/native/scan128-all";id='c1-n128';freq=@(.1,.2,.4,.8,1.6,3.2)},
        @{c=1;n=32;nodes="$study/operands-c1/c1-n32";curve="$prior/native/m3-fourier";id='c1-n32';freq=@(.1,.2,.4,.8,1.6,3.2)},
        @{c=2;n=32;nodes="$study/operands-c2/c2-n32";curve="$prior/native/m4-fourier";id='c2-n32';freq=@(.1,.2,.4,.8,1.6,3.2)},
        @{c=1;n=256;nodes="$study/operands-c1/c1-n256";curve="$cause/single-frequency-native/c1-n256";id='c1-n256-high';freq=@(3.2)},
        @{c=2;n=128;nodes="$study/operands-c2/c2-n128";curve="$cause/single-frequency-native/c2-n128";id='c2-n128-high';freq=@(3.2)},
        @{c=2;n=256;nodes="$study/operands-c2/c2-n256";curve="$cause/single-frequency-native/c2-n256";id='c2-n256-high';freq=@(3.2)})){
        $curve="proof/$($spec.id)/curve.json";$nodes="proof/$($spec.id)/nodes.json"
        Copy-File "$($spec.curve)/data.json" "$stage/$curve"
        Copy-File "$($spec.curve)/captured-settings.json" "$stage/proof/$($spec.id)/captured-settings.json"
        Copy-File "$($spec.nodes)/fourier-operands.json" "$stage/$nodes"
        Copy-File "$($spec.nodes)/fourier-fields.json" "$stage/proof/$($spec.id)/fields.json"
        $checks+=@{configuration=$spec.c;sampling=$spec.n;curve=$curve;nodes=$nodes;curveSha256=(Hash "$stage/$curve");nodesSha256=(Hash "$stage/$nodes");frequencies=$spec.freq}
    }
    Json @{referenceBasis='ConvergenceCheckedNativeResult';referenceMethod='NativeOperandsWithValidatedDisplayInterpolation';
        policySha256=(Hash $policy);inputs=$inputs;displayChecks=$checks;nativeSampling=@(128,256);coreSampling=@(32,64);
        frequenciesCyclesPerMilliradian=@(.1,.2,.4,.8,1.6);calculatedFieldCount=10;scan='+Y';maximumFieldDegrees=1.5;
        wavelength=0;polarization=$false;removeVignetting=$true;productOpticalCalculationChanged=$false;
        originalLowSamplingCapturesPreserved=$true;rawAnalysisBufferReplaced=$false;
        disclosure='The current native whole-plot API timed out. curve.json is explicitly derived from actual MTFT/MTFS Grid=0 amplitude nodes using the display rule verified against 42 complete independent native curves (12600 values); it is not a raw new analysis buffer.'
    } "$stage/contract.json"
    Seal $stage (Join-Path $RepositoryRoot 'validation/zemax/2026-r1/binocular-fourier-remaining-2026-10-31') 'reference.zip'
    return
}
if($Mode -eq 'Evidence') {
    $stage="$study/evidence-stage"
    if(Test-Path -LiteralPath $stage){throw 'Fresh evidence stage required'}
    New-Item -ItemType Directory $stage|Out-Null
    foreach($file in Get-ChildItem -LiteralPath $study -Recurse -File){
        $relative=[IO.Path]::GetRelativePath($study,$file.FullName).Replace('\','/')
        if($relative.StartsWith('evidence-stage/') -or $relative.StartsWith('reference-stage/')){continue}
        Copy-File $file.FullName "$stage/study/$relative"
    }
    $tests=Join-Path $RepositoryRoot $TestDirectory
    foreach($file in Get-ChildItem -LiteralPath $tests -File){Copy-File $file.FullName "$stage/runtime/$($file.Name)"}
    foreach($file in Get-ChildItem -LiteralPath "$tests/TestResults" -File){Copy-File $file.FullName "$stage/tests/$($file.Name)"}
    foreach($fixture in @('binocular-fourier-remaining','binocular-fourier-converged')){
        foreach($file in Get-ChildItem -LiteralPath "$tests/Fixtures/$fixture" -File){Copy-File $file.FullName "$stage/runtime/Fixtures/$fixture/$($file.Name)"}
    }
    foreach($runner in @('binocular-fourier-remaining-runner-20261031-initial','binocular-fourier-remaining-runner-20261031-assessment')){
        foreach($file in Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot "artifacts/validation/$runner") -File){Copy-File $file.FullName "$stage/runners/$runner/$($file.Name)"}
    }
    foreach($folder in @('tools/diagnostics/BinocularFourier20261031','tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost')){
        foreach($file in Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot $folder) -File|Where-Object Extension -in @('.cs','.md','.csproj')){
            Copy-File $file.FullName "$stage/source/$folder/$($file.Name)"
        }
    }
    foreach($file in @('tools/diagnostics/FftProfileNative20261029/Program.cs','tools/validation/Seal-BinocularFourierRemaining.ps1',
        'tests/OpticalSystemDesign.ZemaxComparison.Tests/BinocularFourierRemainingTests.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj')){
        Copy-File (Join-Path $RepositoryRoot $file) "$stage/source/$file"
    }
    foreach($file in Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'artifacts/validation') -File -Filter 'binocular-fourier-remaining-*.log'){
        Copy-File $file.FullName "$stage/logs/$($file.Name)"
    }
    Copy-File $policy "$stage/policy/comparison-settings.json"
    Seal $stage (Join-Path $RepositoryRoot 'validation/rechecks/2026-10-31/binocular-fourier-remaining') 'evidence-current.zip'
}
