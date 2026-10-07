$ErrorActionPreference='Stop'
$taskRepo='D:\Projects\opticalsystem'
$taskRoot=Join-Path $taskRepo 'artifacts/zemax-standard-samples/20261007/tessar-ray-audit/tessar-final-v2-gq12-remove'
function Hash-TaskFile([string]$taskPath) { (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash.ToLowerInvariant() }
$taskManifest=Get-Content (Join-Path $taskRoot 'manifest.json') -Raw | ConvertFrom-Json
$taskCore=Get-Content (Join-Path $taskRoot 'core.json') -Raw | ConvertFrom-Json
$taskNative=Get-Content (Join-Path $taskRoot 'native/ray-audit.json') -Raw | ConvertFrom-Json
$taskEnvironment=Get-Content (Join-Path $taskRoot 'native/environment.json') -Raw | ConvertFrom-Json
if(!$taskManifest.originalFileUnchanged -or $taskManifest.nativeExitCode -ne 0 -or $taskManifest.timedOut -or !$taskEnvironment.validLicense -or $taskEnvironment.major -ne 26 -or $taskEnvironment.minor -ne 1){throw 'Invalid final native capture'}
if((Hash-TaskFile $taskManifest.job.source) -ne $taskManifest.sourceSha256 -or (Hash-TaskFile (Join-Path $taskRoot 'source.zmx')) -ne $taskManifest.sourceSha256){throw 'Final capture source changed'}
if((Hash-TaskFile (Join-Path $taskRoot 'inputs.json')) -ne $taskManifest.inputsSha256 -or (Hash-TaskFile (Join-Path $taskRoot 'snapshot.json')) -ne $taskManifest.snapshotSha256){throw 'Final capture input changed'}
if((Hash-TaskFile (Join-Path $taskRepo 'src/OptilandWorkbench.Core/bin/Release/net10.0/OptilandWorkbench.Core.dll')) -ne $taskManifest.coreAssemblySha256 -or (Hash-TaskFile (Join-Path $taskRepo 'tools/OptilandWorkbench.ZemaxComparison/bin/Release/net10.0/OptilandWorkbench.ZemaxComparison.dll')) -ne $taskManifest.toolAssemblySha256){throw 'Final native audit does not match current default assemblies'}
$taskMaxXY=0d
$taskMaxZ=0d
$taskMaxDirection=0d
$taskMissing=0
$taskCount=0
for($taskFieldIndex=0;$taskFieldIndex -lt $taskCore.Count;$taskFieldIndex++){
    foreach($taskRay in $taskCore[$taskFieldIndex].gaussianPupilIntegral){
        $taskInputIndex=$taskFieldIndex*$taskManifest.pupilCount+$taskRay.pupilIndex
        $taskBySurface=@{}
        foreach($taskSample in $taskRay.samples){$taskBySurface[[int]$taskSample.surfaceNumber]=$taskSample}
        foreach($taskSurface in $taskNative.surfaces){
            $taskCount++
            $taskNativeRay=$taskSurface.rays[$taskInputIndex]
            $taskSample=$taskBySurface[[int]$taskSurface.surface]
            if(!$taskSample -or $taskSample.vignetted -or $taskNativeRay.error -ne 0){$taskMissing++;continue}
            if($taskNativeRay.number -ne $taskInputIndex+1){throw 'Native input ray order differs'}
            $taskMaxXY=[Math]::Max($taskMaxXY,[Math]::Max([Math]::Abs($taskSample.localPosition.x-$taskNativeRay.x),[Math]::Abs($taskSample.localPosition.y-$taskNativeRay.y)))
            $taskMaxZ=[Math]::Max($taskMaxZ,[Math]::Abs($taskSample.localPosition.z-$taskNativeRay.z))
            $taskMaxDirection=[Math]::Max($taskMaxDirection,[Math]::Max([Math]::Abs($taskSample.localDirection.x-$taskNativeRay.l),[Math]::Max([Math]::Abs($taskSample.localDirection.y-$taskNativeRay.m),[Math]::Abs($taskSample.localDirection.z-$taskNativeRay.n))))
        }
    }
}
if($taskManifest.inputCount -ne 576 -or $taskCount -ne 5184 -or $taskMissing -ne 0 -or $taskMaxXY -gt 2e-10 -or $taskMaxZ -gt 2e-10 -or $taskMaxDirection -gt 2e-10){throw 'Final GQ12 per-surface verification failed'}
$taskFiles=@(foreach($taskName in @('manifest.json','source.zmx','snapshot.json','inputs.json','core.json','native/environment.json','native/model.json','native/request.json','native/ray-audit.json')){@{path=$taskName;sha256=(Hash-TaskFile (Join-Path $taskRoot $taskName))}})
$taskResult=@{inputs=$taskManifest.inputCount;surfaceRayResults=$taskCount;missingOrNativeError=$taskMissing;maxXYMillimeters=$taskMaxXY;maxZMillimeters=$taskMaxZ;maxDirectionComponent=$taskMaxDirection;originalFileUnchanged=$true;matchesCurrentDefaultAssemblies=$true;files=$taskFiles}
$taskResult | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $taskRoot 'coordinate-verification.json') -Encoding utf8NoBOM
$taskResult | Select-Object inputs,surfaceRayResults,missingOrNativeError,maxXYMillimeters,maxZMillimeters,maxDirectionComponent | ConvertTo-Json
