param([Parameter(Mandatory)][string]$RunDirectory)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$run=(Resolve-Path -LiteralPath $RunDirectory).Path
$directory=Join-Path $run 'host'
if(Test-Path -LiteralPath $directory){throw 'Use a fresh native host directory'}
New-Item -ItemType Directory -Path $directory | Out-Null
$identities=@()
foreach($file in Get-ChildItem -LiteralPath (Join-Path $repo 'tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost') -Filter '*.cs') {
    $content=[IO.File]::ReadAllText($file.FullName)
    if($file.Name -eq 'Host.cs') {
        $create='app = new ZOSAPI_Connection().CreateNewApplication();'
        $close='if (app != null) try { app.CloseApplication(); } catch (Exception e) { Console.Error.WriteLine(e.Message); }'
        if($content.Split($create).Length -ne 2 -or $content.Split($close).Length -ne 2){throw 'Native lifetime contract changed'}
        $content=$content.Replace($create,'app = NativeBatchHost.App;').Replace($close,'// Application lifetime belongs to NativeBatchHost.')
    }
    $target=Join-Path $directory $file.Name
    [IO.File]::WriteAllText($target,$content)
    $identities+=@{source=$file.FullName;sourceSha256=(Get-FileHash $file.FullName).Hash.ToLowerInvariant();compiledSha256=(Get-FileHash $target).Hash.ToLowerInvariant()}
}
Copy-Item -LiteralPath (Join-Path $repo 'tools/diagnostics/StandardMatrix20261015/NativeBatchHost.cs') -Destination $directory
$first=@(Get-Content (Join-Path $run 'native-jobs.json') -Raw | ConvertFrom-Json)[0]
$api=(Get-Content $first.request -Raw | ConvertFrom-Json).zosApiPath
$exe=Join-Path $directory 'NativeBatchHost.exe'
$arguments=@('/nologo','/target:exe','/platform:x64','/main:NativeBatchHost',('/out:'+$exe),'/r:System.Web.Extensions.dll')
foreach($name in @('ZOSAPI.dll','ZOSAPI_Interfaces.dll','ZOSAPI_NetHelper.dll')){$arguments+=('/r:'+(Join-Path $api $name))}
$arguments+=@(Get-ChildItem -LiteralPath $directory -Filter '*.cs' | ForEach-Object FullName)
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe') @arguments
if($LASTEXITCODE -ne 0){throw 'Native host compilation failed'}
[IO.File]::WriteAllText(($exe+'.config'),'<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>')
ConvertTo-Json -InputObject $identities -Depth 8 | Set-Content -LiteralPath (Join-Path $directory 'source-identities.json') -Encoding utf8
Write-Output $exe
