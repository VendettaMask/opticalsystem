#requires -Version 5.1
[CmdletBinding()]
param([string]$AppVersion = '')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($AppVersion)) {
    [xml]$properties = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'ProductVersion.props') -Raw
    $AppVersion = [string]$properties.Project.PropertyGroup.ProductVersion
}
# Windows installer versions must also be representable in PE version resources.
if ($AppVersion -cnotmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' -or
    @($AppVersion.Split('.') | Where-Object { [decimal]$_ -gt 65534 }).Count -gt 0) {
    throw 'Product version must be a canonical three-part stable version (each component 0..65534).'
}
return $AppVersion
