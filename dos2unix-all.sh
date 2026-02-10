#!/usr/bin/env bash
set -euo pipefail

if ! command -v dos2unix >/dev/null 2>&1; then
  echo "dos2unix is not installed or not on PATH" >&2
  exit 1
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

find "$repo_root" -type f -name "*.sh" -print0 | while IFS= read -r -d '' file; do
  dos2unix "$file" >/dev/null 2>&1 || dos2unix "$file"
done
