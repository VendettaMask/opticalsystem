param([ValidateSet('Reference','Evidence')][string]$Mode,
    [string]$RepositoryRoot=(Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference='Stop'
$root=Join-Path $RepositoryRoot 'artifacts/validation/binocular-fourier-direct-20261031'
$prior=Join-Path $RepositoryRoot 'artifacts/validation/binocular-fourier-remaining-20261031'
function Hash($p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
function Json($v,$p){$v|ConvertTo-Json -Depth 80|Set-Content -LiteralPath $p -Encoding utf8}
function CopyFile($from,$to){New-Item -ItemType Directory -Path (Split-Path $to) -Force|Out-Null;Copy-Item -LiteralPath $from -Destination $to}
function Archive($stage,$destination,$name){
    New-Item -ItemType Directory $destination -Force|Out-Null;$path=Join-Path $destination $name
    if(Test-Path -LiteralPath $path){throw "Immutable archive already exists: $path"}
    [IO.Compression.ZipFile]::CreateFromDirectory($stage,$path,[IO.Compression.CompressionLevel]::Optimal,$false)
    $entries=@();$zip=[IO.Compression.ZipFile]::OpenRead($path)
    try{foreach($e in $zip.Entries|Sort-Object FullName){$s=$e.Open();try{$sha=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($s))}finally{$s.Dispose()};$entries+=@{path=$e.FullName;sha256=$sha;bytes=$e.Length}}}finally{$zip.Dispose()}
    Json @{sha256=(Hash $path);bytes=(Get-Item $path).Length;entries=$entries} "$destination/manifest.json"
    $verified=@();$zip=[IO.Compression.ZipFile]::OpenRead($path)
    try{foreach($r in $entries){$e=$zip.GetEntry($r.path);$s=$e.Open();try{$sha=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($s))}finally{$s.Dispose()};if($sha -ne $r.sha256 -or $e.Length -ne $r.bytes){throw 'Archive readback mismatch'};$verified+=@{path=$r.path;sha256=$sha;bytes=$e.Length;matched=$true}}}finally{$zip.Dispose()}
    Json @{sha256=(Hash $path);allMatched=$true;verifiedEntries=$verified.Count;entries=$verified} "$destination/verification.json"
    Write-Output "$name : $(Hash $path); $($entries.Count) entries; $((Get-Item $path).Length) bytes"
}
if($Mode -eq 'Reference'){
    $stage="$root/reference-stage";if(Test-Path $stage){throw 'Fresh reference stage required'}
    New-Item -ItemType Directory $stage|Out-Null
    $cases=Get-Content "$prior/cases.json" -Raw|ConvertFrom-Json;$inputs=@()
    foreach($c in 1..2){$case=$cases|Where-Object configuration -eq $c|Select-Object -First 1
        if((Hash $case.source) -ne $case.sourceSha256 -or (Hash $case.snapshot) -ne $case.snapshotSha256){throw 'Original physical inputs changed'}
        CopyFile $case.snapshot "$stage/inputs/c$c.optic.json"
        $inputs+=@{configuration=$c;snapshot="inputs/c$c.optic.json";snapshotSha256=$case.snapshotSha256;sourceSha256=$case.sourceSha256}
        foreach($n in @(128,256)){
            foreach($file in Get-ChildItem "$root/direct-default/c$c-n$n" -File){CopyFile $file.FullName "$stage/direct/c$c-n$n/$($file.Name)"}
            CopyFile "$prior/derived-curves/c$c-n$n/curve.json" "$stage/previous-derived/c$c-n$n.json"
        }
    }
    CopyFile $cases[0].source "$stage/inputs/Binocular_System.ZMX"
    CopyFile "$root/direct-default/execution.json" "$stage/direct/execution.json"
    foreach($file in Get-ChildItem "$prior/live-buffer-control/full" -File){CopyFile $file.FullName "$stage/audit/late/full/$($file.Name)"}
    CopyFile "$prior/live-buffer-control/execution.json" "$stage/audit/late/execution.json"
    CopyFile "$prior/full-frequency-bounded/execution.json" "$stage/audit/timeout/execution.json"
    Json @{referenceBasis='ConvergenceCheckedDirectNativeBuffers';inputs=$inputs;nativeSampling=@(128,256);coreSampling=@(32,64);
        frequenciesCyclesPerMilliradian=@(.1,.2,.4,.8,1.6,3.2);fieldDensity=10;scan='+Y';wavelength=0;polarization=$false;removeVignetting=$true;
        policySha256=(Hash (Join-Path $RepositoryRoot 'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json'));
        previousReferenceSha256=(Hash (Join-Path $RepositoryRoot 'validation/zemax/2026-r1/binocular-fourier-remaining-2026-10-31/reference.zip'));
        directBuffers=$true;referenceInterpolationUsed=$false;originalCapturesPreserved=$true
    } "$stage/contract.json"
    Archive $stage (Join-Path $RepositoryRoot 'validation/zemax/2026-r1/binocular-fourier-direct-2026-10-31') 'reference.zip'
    return
}
if($Mode -eq 'Evidence'){
    $stage="$root/evidence-stage";if(Test-Path $stage){throw 'Fresh evidence stage required'}
    New-Item -ItemType Directory $stage|Out-Null
    foreach($file in Get-ChildItem $root -Recurse -File){$relative=[IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/');if($relative.StartsWith('evidence-stage/') -or $relative.StartsWith('reference-stage/')){continue};CopyFile $file.FullName "$stage/study/$relative"}
    $test=Join-Path $RepositoryRoot 'artifacts/validation/binocular-fourier-direct-tests-20261031'
    foreach($file in Get-ChildItem $test -File){CopyFile $file.FullName "$stage/runtime/$($file.Name)"}
    foreach($file in Get-ChildItem "$test/TestResults" -File){CopyFile $file.FullName "$stage/tests/$($file.Name)"}
    foreach($fixture in @('binocular-fourier-direct','binocular-fourier-converged')){foreach($file in Get-ChildItem "$test/Fixtures/$fixture" -File){CopyFile $file.FullName "$stage/runtime/Fixtures/$fixture/$($file.Name)"}}
    $runner=Join-Path $RepositoryRoot 'artifacts/validation/binocular-fourier-direct-runner-20261031'
    foreach($file in Get-ChildItem $runner -File){CopyFile $file.FullName "$stage/runner/$($file.Name)"}
    foreach($file in Get-ChildItem (Join-Path $RepositoryRoot 'artifacts/validation/binocular-fourier-direct-native-runner-20261031') -File){CopyFile $file.FullName "$stage/native-runner/$($file.Name)"}
    foreach($file in Get-ChildItem (Join-Path $RepositoryRoot 'artifacts/validation') -File -Filter 'binocular-fourier-direct-*.log'){CopyFile $file.FullName "$stage/logs/$($file.Name)"}
    foreach($file in @('direct-assessment.json','assessment-f5b26dd57e12.json','runtime-identity.json','failure-events.json')){if(Test-Path "$prior/$file"){CopyFile "$prior/$file" "$stage/previous/$file"}}
    foreach($file in Get-ChildItem "$prior/core-f5b26dd57e12" -File -Recurse){CopyFile $file.FullName "$stage/previous/core-f5b26dd57e12/$([IO.Path]::GetRelativePath("$prior/core-f5b26dd57e12",$file.FullName))"}
    foreach($relative in @('tools/OpticalSystemDesign.ZemaxComparison/Diagnostics/FftFieldCaptureAudit.cs','tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/Host.cs',
        'tools/diagnostics/BinocularFourier20261031/DirectReferenceAssessment.cs','tools/diagnostics/BinocularFourier20261031/Program.cs',
        'tests/OpticalSystemDesign.ZemaxComparison.Tests/BinocularFourierDirectTests.cs','tools/validation/Seal-BinocularFourierDirect.ps1',
        'tools/diagnostics/FftProfileNative20261029/Program.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj',
        'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json')){CopyFile (Join-Path $RepositoryRoot $relative) "$stage/source/$relative"}
    Archive $stage (Join-Path $RepositoryRoot 'validation/rechecks/2026-10-31/binocular-fourier-direct') 'evidence-current.zip'
}
