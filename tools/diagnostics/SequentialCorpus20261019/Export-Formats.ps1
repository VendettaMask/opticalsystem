param([Parameter(Mandatory)][string]$RunDirectory, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$run = (Resolve-Path -LiteralPath $RunDirectory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh export directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$inventory = Get-Content -LiteralPath (Join-Path $run 'inventory.json') -Raw | ConvertFrom-Json
$jobs = @($inventory.files | Where-Object { $_.extension -in @('.zmx','.zos') } | ForEach-Object {
    $format = $_.extension.TrimStart('.')
    [pscustomobject]@{ id=$_.id; input=(Join-Path (Join-Path $run 'sources') $_.id); output=(Join-Path (Join-Path $output $format) ([IO.Path]::ChangeExtension($_.id,'.zmx'))) }
})
$jobPath = Join-Path $output 'export-jobs.json'
$jobs | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $jobPath -Encoding utf8
$first = @(Get-Content -LiteralPath (Join-Path $run 'native-jobs.json') -Raw | ConvertFrom-Json)[0]
$api = (Get-Content -LiteralPath $first.request -Raw | ConvertFrom-Json).zosApiPath
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe = Join-Path $output 'NativeFormatExport.exe'
$compileArgs = @('/nologo','/target:exe','/platform:x64',('/out:'+$exe),'/r:System.Web.Extensions.dll')
foreach ($name in @('ZOSAPI.dll','ZOSAPI_Interfaces.dll','ZOSAPI_NetHelper.dll')) { $compileArgs += '/r:' + (Join-Path $api $name) }
$compileArgs += Join-Path $PSScriptRoot 'NativeTools/NativeFormatExport.cs'
& $compiler @compileArgs 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
[IO.File]::WriteAllText(($exe+'.config'),'<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>')
$env:PATH = $api + ';' + $env:PATH
& $exe $api $jobPath $output 2>&1 | Tee-Object -FilePath (Join-Path $output 'export.log')
exit $LASTEXITCODE
