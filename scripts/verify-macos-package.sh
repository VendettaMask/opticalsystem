#!/usr/bin/env bash
set -euo pipefail
bundle="${1:?Application bundle required}"
output="${2:?Fresh verification directory required}"
[[ ! -e "$output" ]] || { echo 'Verification output already exists.' >&2; exit 1; }
mkdir -p "$output"
output="$(cd "$output" && pwd)"
bundle="$(cd "$bundle" && pwd)"
payload="$bundle/Contents/MacOS"
for module in Core Application InitialStructure.Contracts InitialStructure.Engine InitialStructure.Persistence CoatingDesign.Engine Modules; do
  test -s "$payload/OpticalSystemDesign.$module.dll" || { echo "Missing built-in library: $module" >&2; exit 1; }
done
for required in OpticalSystemDesign.App OpticalSystemDesign.App.dll OpticalSystemDesign.App.runtimeconfig.json \
  libhostfxr.dylib libcoreclr.dylib libSkiaSharp.dylib libHarfBuzzSharp.dylib libAvaloniaNative.dylib \
  LensLibrary/index.json Assets/Fonts/Typography/Source-Han-Sans-OFL.txt \
  Assets/Fonts/Typography/DM-Sans-OFL.txt Assets/Fonts/Typography/JetBrains-Mono-OFL.txt \
  Licenses/AutoLens-LICENSE.txt Licenses/ILGPU-LICENSE.txt Licenses/TFStudio-LICENSE.txt Licenses/tmmcore-LICENSE.txt; do
  test -s "$payload/$required" || { echo "Missing payload: $required" >&2; exit 1; }
done
test -d "$payload/LensLibrary/StockCatalogs"
test -x "$payload/OpticalSystemDesign.App"
test -s "$bundle/Contents/Resources/AppIcon.icns"
if find "$bundle/Contents" -type f \( -name 'OpticalSystemDesign.InitialStructure.App*' -o -name 'OpticalSystemDesign.CoatingDesign.App*' \) | grep -q .; then
  echo 'Independent module executables are not permitted.' >&2; exit 1
fi
while IFS= read -r -d '' native; do
  if /usr/bin/file -b "$native" | /usr/bin/grep -q 'Mach-O'; then
    [[ "$(/usr/bin/lipo -archs "$native")" == arm64 ]] || { echo "Unexpected native architecture: $native" >&2; exit 1; }
  fi
  if /usr/bin/file -b "$native" | /usr/bin/grep -q 'Mach-O'; then
    /usr/bin/codesign --verify --strict "$native"
  fi
done < <(find "$bundle/Contents" -type f -print0)
/usr/bin/codesign --verify --deep --strict "$bundle"
/usr/bin/plutil -lint "$bundle/Contents/Info.plist"
# Existing product verification runs before the GUI and never loads user data.
"$payload/OpticalSystemDesign.App" --verify-installation "$output/native-product.json"
python3 - "$bundle" "$output" <<'PY'
import hashlib, json, pathlib, plistlib, sys
bundle, output = map(pathlib.Path, sys.argv[1:])
report = json.loads((output/'native-product.json').read_text())
assert report['Success'] and report['Architecture']=='Arm64' and report['ModulesInProcess']
assert report['Modules']==['InitialStructure','CoatingDesign'] and len(report['ModuleLibraries'])==5
assert not report['UserDataWritten'] and not report['GuiVerified']
info = plistlib.loads((bundle/'Contents/Info.plist').read_bytes())
assert info['CFBundleShortVersionString']==report['Version'].split('+')[0]
assert info['CFBundleVersion']==info['CFBundleShortVersionString']
assert info['LSArchitecturePriority']==['arm64']
files = {str(p.relative_to(bundle)):hashlib.file_digest(p.open('rb'), 'sha256').hexdigest()
         for p in sorted(bundle.rglob('*')) if p.is_file()}
(output/'bundle-manifest.json').write_text(json.dumps(files,indent=2)+'\n')
(output/'summary.json').write_text(json.dumps(dict(Architecture='Arm64', ManifestFiles=len(files),
    SelfContained=True, BothModulesInProcess=True, NativeCodeSignaturesVerified=True,
    DeveloperIdSigned=False, Notarized=False, GuiVerified=False, UserDataWritten=False),indent=2)+'\n')
print(f'Verified Apple Silicon package: {len(files)} files; both built-in modules in the main process.')
PY
