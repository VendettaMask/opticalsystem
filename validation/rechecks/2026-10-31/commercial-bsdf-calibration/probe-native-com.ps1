$ErrorActionPreference='Stop'
$viewerName='BSDF_BRDF_Anisotropic_Viewer'
$before=@(Get-Process -Name $viewerName -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
if($before.Count){throw 'An existing BSDF viewer is in use; do not attach or alter it.'}
$viewer=$null; $ownedPid=0
try {
 $viewer=New-Object -ComObject 'SimpleBSDFSurfaceViewer.Application261'
 $ownedPid=$viewer.GetPID(); $viewer.Show(0) | Out-Null
 $created=@(Get-Process -Name $viewerName -ErrorAction SilentlyContinue | Where-Object {$_.Id-notin$before})
 if(@($created|Where-Object {$_.MainWindowHandle-ne0}).Count){throw 'Viewer exposed a window; no acceptance capture.'}
 $file=(Resolve-Path 'tests/OpticalSystemDesign.Tests/Fixtures/speos-r-test-v9.anisotropicbsdf').Path
 $opened=$viewer.OpenFile($file)
 $value=0.0
 $ok=$viewer.GetReflectionCoefficient(6.0,0.0,550.0,[ref]$value)
 [pscustomobject]@{Backend='Speos Viewer COM';Version='2026.1.0.37094';File=$file;Opened=$opened;QuerySuccess=$ok;Reflection=$value;HeadlessRequested=$true} | ConvertTo-Json | Set-Content 'validation/rechecks/2026-10-31/commercial-bsdf-calibration/native-com-probe.json'
 Get-Content 'validation/rechecks/2026-10-31/commercial-bsdf-calibration/native-com-probe.json'
} catch {
 [pscustomobject]@{Backend='Speos Viewer COM';Version='2026.1.0.37094';HResult=$_.Exception.HResult;Error=$_.Exception.Message;CommercialPointwiseComparisonComplete=$false;NativeReferenceCount=0;RegistrationChanged=$false;RpcServerInstalled=(Test-Path 'D:\Program Files\ANSYS Inc\v261\Optical Products\SPEOS_RPC\SpeosRPC_Server.exe')} | ConvertTo-Json | Set-Content 'validation/rechecks/2026-10-31/commercial-bsdf-calibration/native-com-probe.json'
} finally {
 if($null-ne$viewer){[Runtime.InteropServices.Marshal]::FinalReleaseComObject($viewer)|Out-Null}
 if($ownedPid -gt 0 -and $ownedPid -notin $before){$owned=Get-Process -Id $ownedPid -ErrorAction SilentlyContinue;if($owned.ProcessName -eq $viewerName){$owned.Kill()}}
}

