#!/usr/bin/env bash
set -euo pipefail
root=/workspace/opticalsystem-cloud
mkdir -p "$root/downloads" "$root/dotnet" "$root/powershell" "$root/bin" "$root/nuget"
cd "$root/downloads"
fetch() { if [ ! -f "$2" ]; then curl --fail --location --retry 3 "$1" --output "$2"; fi; }
fetch https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.300/dotnet-sdk-10.0.300-linux-x64.tar.gz dotnet-sdk-10.0.300-linux-x64.tar.gz
printf '%s  %s\n' a0c404c1a2f85d70e32392ce297eb388c0310c519521b538a031a895469444c67f347d4f9ca1f8441f525967a89c9b75e2cd1676da486f95118cf4025c38d904 dotnet-sdk-10.0.300-linux-x64.tar.gz | sha512sum --check
if [ ! -x "$root/dotnet/dotnet" ]; then tar -xzf dotnet-sdk-10.0.300-linux-x64.tar.gz -C "$root/dotnet"; fi
fetch https://github.com/PowerShell/PowerShell/releases/download/v7.6.9/powershell-7.6.9-linux-x64.tar.gz powershell-7.6.9-linux-x64.tar.gz
printf '%s  %s\n' 1da6d6d6035d651f20e70ddee7c4c15ac6ab8b9fb40ee92240d91263f28a884b powershell-7.6.9-linux-x64.tar.gz | sha256sum --check
if [ ! -f "$root/powershell/pwsh" ]; then tar -xzf powershell-7.6.9-linux-x64.tar.gz -C "$root/powershell"; fi
chmod +x "$root/powershell/pwsh"
ln -sfn "$root/dotnet/dotnet" "$root/bin/dotnet"
ln -sfn "$root/powershell/pwsh" "$root/bin/pwsh"
cat > "$root/activate.sh" <<'ACTIVATE'
export DOTNET_ROOT=/workspace/opticalsystem-cloud/dotnet
export PATH="/workspace/opticalsystem-cloud/bin:$PATH"
export DOTNET_CLI_HOME=/workspace/opticalsystem-cloud/dotnet-home
export NUGET_PACKAGES=/workspace/opticalsystem-cloud/nuget
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export POWERSHELL_TELEMETRY_OPTOUT=1
export XDG_DATA_HOME=/workspace/opticalsystem-cloud/xdg/data
export XDG_CONFIG_HOME=/workspace/opticalsystem-cloud/xdg/config
export XDG_CACHE_HOME=/workspace/opticalsystem-cloud/xdg/cache
ACTIVATE
source "$root/activate.sh"
cd /workspace/opticalsystem
dotnet --version
pwsh --version
git --version
dotnet restore OpticalSystemDesign.slnx --locked-mode -p:ContinuousIntegrationBuild=true
