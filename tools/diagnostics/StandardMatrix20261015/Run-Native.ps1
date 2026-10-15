param(
    [Parameter(Mandatory=$true)][string]$RunDirectory,
    [string]$ApiDirectory=''
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$run=(Resolve-Path -LiteralPath $RunDirectory).Path
if (!$ApiDirectory) {
    $firstJob=@(Get-Content -LiteralPath (Join-Path $run 'native-jobs.json') -Raw | ConvertFrom-Json)[0]
    $ApiDirectory=(Get-Content -LiteralPath $firstJob.request -Raw | ConvertFrom-Json).zosApiPath
}
$hostDirectory=Join-Path $run 'host'
if (Test-Path -LiteralPath $hostDirectory) { throw 'Use a fresh native-host directory.' }
New-Item -ItemType Directory -Path $hostDirectory | Out-Null
$sourceDirectory=Join-Path $repo 'tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost'
$sourceRecords=@()
foreach ($file in Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs') {
    $content=[IO.File]::ReadAllText($file.FullName)
    if ($file.Name -eq 'Host.cs') {
        $create='app = new ZOSAPI_Connection().CreateNewApplication();'
        $close='if (app != null) try { app.CloseApplication(); } catch (Exception e) { Console.Error.WriteLine(e.Message); }'
        if ($content.Split($create).Length -ne 2 -or $content.Split($close).Length -ne 2) { throw 'Host lifetime contract changed; inspect before patching.' }
        $content=$content.Replace($create,'app = NativeBatchHost.App;').Replace($close,'// Application lifetime belongs to NativeBatchHost; analysis.Close above is unchanged.')
    }
    $target=Join-Path $hostDirectory $file.Name
    [IO.File]::WriteAllText($target,$content)
    $sourceRecords+=@{source=$file.FullName;sourceSha256=(Get-FileHash -LiteralPath $file.FullName).Hash;compiled=$target;compiledSha256=(Get-FileHash -LiteralPath $target).Hash}
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeBatchHost.cs') -Destination $hostDirectory
$sourceRecords | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $hostDirectory 'source-identities.json') -Encoding utf8NoBOM
$exe=Join-Path $hostDirectory 'NativeBatchHost.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$compileArgs=@('/nologo','/target:exe','/platform:x64','/main:NativeBatchHost',('/out:'+$exe),'/r:System.Web.Extensions.dll')
foreach ($name in @('ZOSAPI.dll','ZOSAPI_Interfaces.dll','ZOSAPI_NetHelper.dll')) { $compileArgs+=('/r:'+(Join-Path $ApiDirectory $name)) }
$compileArgs+=@(Get-ChildItem -LiteralPath $hostDirectory -Filter '*.cs' | ForEach-Object FullName)
& $compiler @compileArgs 2>&1 | Tee-Object -FilePath (Join-Path $hostDirectory 'build.log')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
[IO.File]::WriteAllText(($exe+'.config'),'<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>')
$env:PATH=$ApiDirectory+';'+$env:PATH
& $exe $ApiDirectory (Join-Path $run 'native-jobs.json') 2>&1 | Tee-Object -FilePath (Join-Path $run 'native.log')
exit $LASTEXITCODE
