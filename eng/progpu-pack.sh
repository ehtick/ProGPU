#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${repo_root}/eng/progpu-package-list.sh"

dotnet="${repo_root}/.dotnet/dotnet"
if [[ ! -x "${dotnet}" ]]; then
  dotnet="dotnet"
fi

configuration="${PROGPU_CONFIGURATION:-Release}"
package_version="${PROGPU_PACKAGE_VERSION:-11.0.0-dev}"
package_output="${PROGPU_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/${configuration}}"

mkdir -p "${package_output}"

echo "Packing ProGPU ${package_version} packages to ${package_output}..."
for index in "${!progpu_package_ids[@]}"; do
  package_id="${progpu_package_ids[$index]}"
  project="${progpu_package_projects[$index]}"

  rm -f \
    "${package_output}/${package_id}.${package_version}.nupkg" \
    "${package_output}/${package_id}.${package_version}.snupkg"

  "${dotnet}" pack "${repo_root}/${project}" \
    --configuration "${configuration}" \
    --output "${package_output}" \
    --verbosity minimal \
    -p:ContinuousIntegrationBuild=true \
    -p:IncludeSymbols=true \
    -p:SymbolPackageFormat=snupkg \
    -p:Version="${package_version}" \
    -p:PackageVersion="${package_version}"

  if [[ ! -f "${package_output}/${package_id}.${package_version}.nupkg" ]]; then
    echo "Expected package was not produced: ${package_output}/${package_id}.${package_version}.nupkg" >&2
    exit 1
  fi
done

echo "ProGPU NuGet package build succeeded for ${package_version}."
