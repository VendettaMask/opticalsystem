#!/usr/bin/env bash
set -euo pipefail
dependency="${1:?Dependency directory required}"
mkdir -p "$dependency"
archive="$dependency/Sparkle-2.10.1.tar.xz"
if [[ ! -f "$archive" ]]; then
  curl --fail --location --proto '=https' --tlsv1.2 --retry 3 \
    https://github.com/sparkle-project/Sparkle/releases/download/2.10.1/Sparkle-2.10.1.tar.xz -o "$archive"
fi
echo '1a856d2a9edb8e628de88121d7be04cf0e2167f8641650b78fca00b14244cf27  '"$archive" | shasum -a 256 -c -
if [[ ! -d "$dependency/Sparkle-2.10.1" ]]; then
  mkdir "$dependency/Sparkle-2.10.1"
  tar -xf "$archive" -C "$dependency/Sparkle-2.10.1"
fi
