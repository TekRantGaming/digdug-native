#!/usr/bin/env bash
# Safety net: fail if anything that looks like a game ROM is about to be published.
# Usage: check-no-roms.sh <file-list-file>      (one path per line)
set -euo pipefail
pattern='(^|/)(dd1a?\.[0-9a-z]+|136007\.[0-9]+|51xx\.bin|53xx\.bin|digdug[^/]*\.zip|[^/]*\.rom)$|(^|/)roms/'
if grep -Eiq "$pattern" "$1"; then
  echo "ERROR: ROM-like files found in the package/repository:"
  grep -Ei "$pattern" "$1"
  exit 1
fi
echo "OK: no ROM-like files in $(wc -l < "$1") entries"
