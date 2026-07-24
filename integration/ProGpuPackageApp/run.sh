#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
project="${repo_root}/integration/ProGpuPackageApp/ProGpuPackageApp.csproj"
mode="${1:-nuget}"
configuration="${PROGPU_CONFIGURATION:-Release}"
integration_version="${PROGPU_INTEGRATION_PACKAGE_VERSION:-12.0.5-preview.19}"
avalonia_version="${PROGPU_AVALONIA_PACKAGE_VERSION:-12.0.5}"
runtime_version="${PROGPU_RUNTIME_PACKAGE_VERSION:-0.1.0-preview.26}"
package_source="${PROGPU_PACKAGE_SOURCE:-${repo_root}/artifacts/packages/${configuration}}"
working_directory="$(mktemp -d "${TMPDIR:-/tmp}/progpu-package-app.XXXXXX")"
consumer_artifacts="${working_directory}/artifacts"

if [[ "$#" -gt 0 ]]; then
  shift
fi

cleanup() {
  local exit_code=$?
  rm -rf "${working_directory}"
  return "${exit_code}"
}
trap cleanup EXIT

dotnet="${repo_root}/.dotnet/dotnet"
if [[ ! -x "${dotnet}" ]]; then
  dotnet="dotnet"
fi

"${dotnet}" new nugetconfig --output "${working_directory}" --force >/dev/null

case "${mode}" in
  local)
    mkdir -p "${package_source}"

    if [[ ! -f "${package_source}/ProGPU.Backend.${runtime_version}.nupkg" ]]; then
      PROGPU_CONFIGURATION="${configuration}" \
      PROGPU_PACKAGE_VERSION="${runtime_version}" \
      PROGPU_PACKAGE_OUTPUT="${package_source}" \
      PROGPU_PACKAGE_GROUP=portable \
        "${repo_root}/eng/progpu-pack.sh"
    fi

    PROGPU_CONFIGURATION="${configuration}" \
    PROGPU_PACKAGE_OUTPUT="${package_source}" \
      "${repo_root}/scripts/progpu-pack.sh"
    # SDK templates have used both `nuget` and `nuget.org` for the default
    # source name. Remove either spelling so the explicitly ordered clean
    # consumer sources below remain deterministic.
    "${dotnet}" nuget remove source nuget \
      --configfile "${working_directory}/nuget.config" >/dev/null 2>&1 || true
    "${dotnet}" nuget remove source nuget.org \
      --configfile "${working_directory}/nuget.config" >/dev/null 2>&1 || true
    "${dotnet}" nuget add source "${package_source}" \
      --name progpu-local \
      --configfile "${working_directory}/nuget.config" >/dev/null
    "${dotnet}" nuget add source https://api.nuget.org/v3/index.json \
      --name nuget \
      --configfile "${working_directory}/nuget.config" >/dev/null
    ;;
  nuget)
    ;;
  *)
    echo "Usage: $0 [local|nuget] [application arguments...]" >&2
    exit 2
    ;;
esac

export NUGET_HTTP_CACHE_PATH="${working_directory}/http-cache"
packages_path="${working_directory}/packages"

"${dotnet}" restore "${project}" \
  --packages "${packages_path}" \
  --artifacts-path "${consumer_artifacts}" \
  --configfile "${working_directory}/nuget.config" \
  --force \
  --no-cache \
  --verbosity minimal \
  "-p:ProGpuIntegrationPackageVersion=${integration_version}" \
  "-p:ProGpuAvaloniaPackageVersion=${avalonia_version}"

if [[ "${PROGPU_INTEGRATION_BUILD_ONLY:-0}" == 1 ]]; then
  "${dotnet}" build "${project}" \
    --configuration "${configuration}" \
    --artifacts-path "${consumer_artifacts}" \
    --no-restore \
    --verbosity minimal \
    "-p:RestorePackagesPath=${packages_path}" \
    "-p:ProGpuIntegrationPackageVersion=${integration_version}" \
    "-p:ProGpuAvaloniaPackageVersion=${avalonia_version}"
else
  "${dotnet}" run \
    --project "${project}" \
    --configuration "${configuration}" \
    --artifacts-path "${consumer_artifacts}" \
    --no-restore \
    "-p:RestorePackagesPath=${packages_path}" \
    "-p:ProGpuIntegrationPackageVersion=${integration_version}" \
    "-p:ProGpuAvaloniaPackageVersion=${avalonia_version}" \
    -- "$@"
fi
