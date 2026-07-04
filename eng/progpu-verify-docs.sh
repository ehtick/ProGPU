#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${repo_root}/eng/progpu-package-list.sh"

require_text() {
  local file="$1"
  local text="$2"
  if ! grep -Fq "${text}" "${repo_root}/${file}"; then
    echo "Missing '${text}' in ${file}." >&2
    exit 1
  fi
}

require_text ".github/workflows/build.yml" "./eng/progpu-pack.sh"
require_text ".github/workflows/release.yml" "NUGET_API_KEY"
require_text "docs/release.md" "NuGet"

for package_id in "${progpu_package_ids[@]}"; do
  require_text "README.md" "| \`${package_id}\` |"
  require_text "docs/release.md" "\`${package_id}\`"
done

echo "ProGPU documentation/package table verification succeeded."
