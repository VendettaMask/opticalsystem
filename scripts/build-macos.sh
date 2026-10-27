#!/usr/bin/env bash
set -euo pipefail

repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runtime=osx-arm64
output_root="$repository/artifacts/macos"
test_package=false
plan_only=false
while (($#)); do
  case "$1" in
    --runtime) runtime="${2:?Missing runtime}"; shift 2 ;;
    --output-root) output_root="${2:?Missing output directory}"; shift 2 ;;
    --test-package) test_package=true; shift ;;
    --plan) plan_only=true; shift ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done
[[ "$runtime" == osx-arm64 ]] || { echo 'Only osx-arm64 (Apple Silicon) is supported.' >&2; exit 2; }
[[ "$(uname -s)" == Darwin && "$(uname -m)" == arm64 ]] || { echo 'Build on an Apple Silicon Mac.' >&2; exit 2; }
cd "$repository"
export AVALONIA_TELEMETRY_OPTOUT=1
project=src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj
version="$(dotnet msbuild "$project" -nologo -getProperty:Version)"
[[ "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]] || { echo 'Invalid product version.' >&2; exit 1; }
suffix=AdHoc
if $test_package; then suffix=AdHocTest; fi
name="OpticalSystemDesign-$version-$runtime-$suffix.dmg"
if $plan_only; then
  printf 'Runtime: %s\nVersion: %s\nPackage: %s\nOutput root: %s\n' "$runtime" "$version" "$name" "$output_root"
  exit 0
fi
mkdir -p "$output_root"
output_root="$(cd "$output_root" && pwd)"
stage="$(mktemp -d "$output_root/package.XXXXXXXX")"
mv "$stage" "$stage.partial"
stage="$stage.partial"
bundle="$stage/image/Optical System Design.app"
payload="$bundle/Contents/MacOS"
mkdir -p "$payload" "$bundle/Contents/Resources"

# Keep platform-specific dependency locks in obj, never rewrite committed locks.
dotnet restore "$project" --locked-mode --nologo
dotnet restore "$project" -r "$runtime" -p:SelfContained=true -p:RestoreLockedMode=false \
  "-p:NuGetLockFilePath=obj/packages.$runtime.lock.json" --nologo
dotnet publish "$project" -c Release -r "$runtime" --self-contained true --no-restore --nologo \
  --output "$payload" -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishAot=false \
  -p:UseAppHost=true -p:DebugType=None -p:DebugSymbols=false

cp packaging/macos/Info.plist "$bundle/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $version" "$bundle/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $version" "$bundle/Contents/Info.plist"
/usr/libexec/PlistBuddy -c 'Add :LSArchitecturePriority array' "$bundle/Contents/Info.plist"
/usr/libexec/PlistBuddy -c 'Add :LSArchitecturePriority:0 string arm64' "$bundle/Contents/Info.plist"
cp src/OpticalSystemDesign.App/Assets/Brand/AppIcon.icns "$bundle/Contents/Resources/AppIcon.icns"
bash scripts/prepare-macos-updater.sh "$output_root/.dependencies" "$bundle"
bash scripts/test-macos-updater.sh "$output_root/.dependencies" "$stage/native-updater-tests"
cp src/OpticalSystemDesign.App/Assets/Icons/LUCIDE-LICENSE.txt "$payload/Assets/Icons/"
chmod +x "$payload/OpticalSystemDesign.App"

# macOS code directories cannot contain unsigned managed DLLs or data files.
# Keep apphost-relative paths through internal links, with data in Resources
# and native libraries in Frameworks (the standard bundle layout).
mkdir -p "$bundle/Contents/Frameworks"
for entry in "$payload"/*; do
  entry_name="$(basename "$entry")"
  if [[ -f "$entry" ]] && /usr/bin/file -b "$entry" | /usr/bin/grep -q 'Mach-O'; then
    if [[ "$entry" != *.dylib ]]; then continue; fi
    destination=Frameworks
  else
    destination=Resources
  fi
  mv "$entry" "$bundle/Contents/$destination/$entry_name"
  ln -s "../$destination/$entry_name" "$entry"
  if [[ "$destination" == Frameworks ]]; then
    # The host resolves the managed entry point into Resources; its app base
    # must also resolve the self-contained runtime without a system .NET SDK.
    ln -s "../Frameworks/$entry_name" "$bundle/Contents/Resources/$entry_name"
  fi
done

# Thin universal native dependencies to arm64 and sign nested code before the
# outer bundle. Ad-hoc integrity signing is not Developer ID or notarization.
while IFS= read -r -d '' native; do
  if /usr/bin/file -b "$native" | /usr/bin/grep -q 'Mach-O'; then
    architectures="$(/usr/bin/lipo -archs "$native")"
    [[ " $architectures " == *' arm64 '* ]] || { echo "Non-arm64 native code: $native" >&2; exit 1; }
    if [[ "$architectures" != arm64 ]]; then
      /usr/bin/lipo "$native" -thin arm64 -output "$native.arm64"
      chmod "$(stat -f '%Lp' "$native")" "$native.arm64"
      mv "$native.arm64" "$native"
    fi
  fi
  if /usr/bin/file -b "$native" | /usr/bin/grep -q 'Mach-O'; then
    /usr/bin/codesign --force --sign - --timestamp=none "$native"
  fi
done < <(find "$bundle/Contents" -type f -print0)
# Seal nested Sparkle XPC services, its installer app and framework inside out.
while IFS= read -r -d '' nested; do
  /usr/bin/codesign --force --sign - --timestamp=none "$nested"
done < <(find "$bundle/Contents/Frameworks" -depth -type d \( -name '*.xpc' -o -name '*.app' -o -name '*.framework' \) -print0)
/usr/bin/codesign --force --sign - --timestamp=none "$bundle"
bash scripts/verify-macos-package.sh "$bundle" "$stage/verification"
ln -s /Applications "$stage/image/Applications"
cp packaging/macos/README.txt "$stage/image/安装说明.txt"
/usr/bin/hdiutil create -volname 'Optical System Design' -srcfolder "$stage/image" -format UDZO -ov "$stage/$name"
/usr/bin/hdiutil verify "$stage/$name"
mkdir "$stage/mounted"
mounted=false
detach_image() { if $mounted; then /usr/bin/hdiutil detach "$stage/mounted"; fi; }
trap detach_image EXIT
/usr/bin/hdiutil attach -readonly -nobrowse -mountpoint "$stage/mounted" "$stage/$name"
mounted=true
bash scripts/verify-macos-package.sh "$stage/mounted/Optical System Design.app" "$stage/dmg-verification"
cmp "$stage/verification/bundle-manifest.json" "$stage/dmg-verification/bundle-manifest.json"
test "$(readlink "$stage/mounted/Applications")" == /Applications
/usr/bin/hdiutil detach "$stage/mounted"
mounted=false
trap - EXIT

python3 - "$repository" "$stage" "$version" "$test_package" "$name" <<'PY'
import hashlib, json, os, pathlib, plistlib, subprocess, sys, uuid
root, stage = map(pathlib.Path, sys.argv[1:3])
version, test, name = sys.argv[3:]
head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
dirty = bool(subprocess.check_output(['git', 'status', '--porcelain', '--untracked-files=normal'], cwd=root, text=True).strip())
record = dict(Version=version, Runtime='osx-arm64', TestPackage=test=='true', SourceCommit=head,
              SourceDirty=dirty, ValidationHooks=False, Signed=False, MacCodeSignature='AdHoc', Notarized=False,
              ModulesInProcess=True, RequiredModules=['InitialStructure','CoatingDesign'],
              ProductAppId='OpticalSystemDesign-8D387EA8-F349-447F-B5D3-B58E27F46D4A',
              ReleaseId=f'{version}-osx-arm64-{uuid.uuid4().hex[:12]}',
              OtaBuildSequence=int(os.environ.get('GITHUB_RUN_ID', '0')),
              MacBundleVersion=plistlib.loads((stage/'image/Optical System Design.app/Contents/Info.plist').read_bytes())['CFBundleVersion'],
              MacOta=True, MacOtaFramework='Sparkle 2.10.1',
              PackageSha256=hashlib.file_digest((stage/name).open('rb'), 'sha256').hexdigest())
(stage/'release-osx-arm64.json').write_text(json.dumps(record, indent=2)+'\n')
PY
completed="${stage%.partial}"
mv "$stage" "$completed"
printf '\nPackage: %s\nRecord: %s\n' "$completed/$name" "$completed/release-osx-arm64.json"
