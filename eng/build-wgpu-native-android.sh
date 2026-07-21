#!/usr/bin/env bash
set -euo pipefail

# Builds the exact wgpu-native ABI consumed by Silk.NET.WebGPU 2.23.0.
# Source remains an external build input under artifacts/ and is never vendored into ProGPU.

usage() {
  cat <<'EOF'
Usage: ./eng/build-wgpu-native-android.sh [arm64|x64|all] [--api LEVEL]

Builds arm64-v8a by default. Pass x64 for an Android emulator library or all
for both ABIs. ANDROID_NDK_ROOT (or ANDROID_NDK_HOME) must identify an Android
NDK. If neither is set, the script searches the ndk/ directory below
ANDROID_SDK_ROOT or ANDROID_HOME.

Environment overrides:
  ANDROID_API_LEVEL          Minimum Android API level (default: 24)
  WGPU_NATIVE_SOURCE         External wgpu-native clone
  WGPU_NATIVE_ANDROID_OUTPUT Packaged headers, libraries, and build manifest
  WGPU_NATIVE_ANDROID_TARGET Cargo build cache (outside the packaged directory)
EOF
}

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_dir="${WGPU_NATIVE_SOURCE:-${repo_root}/artifacts/wgpu-native-src}"
output_dir="${WGPU_NATIVE_ANDROID_OUTPUT:-${repo_root}/artifacts/wgpu-native-android}"
target_dir="${WGPU_NATIVE_ANDROID_TARGET:-${repo_root}/artifacts/wgpu-native-android-build}"
android_api_level="${ANDROID_API_LEVEL:-24}"

# Silk.NET 2.23 still exposes the WebGPU C ABI used by wgpu-native's
# May 2024 header update (with wgpu-core 0.19.4). A newer native library is not
# ABI-compatible with the generated Silk.NET.WebGPU assembly.
expected_commit="33133da4ec5a0174cb21539ef2d3346f75200411"
upstream_url="https://github.com/gfx-rs/wgpu-native.git"

requested_architectures=()
while (($# > 0)); do
  case "$1" in
    arm64|--arm64)
      requested_architectures+=("arm64")
      ;;
    x64|--x64)
      requested_architectures+=("x64")
      ;;
    all|--all)
      requested_architectures+=("arm64" "x64")
      ;;
    --api)
      if (($# < 2)); then
        echo "--api requires a numeric Android API level." >&2
        exit 2
      fi
      android_api_level="$2"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
  shift
done

if ((${#requested_architectures[@]} == 0)); then
  requested_architectures=("arm64")
fi

if [[ ! "${android_api_level}" =~ ^[0-9]+$ ]] || ((android_api_level < 24)); then
  echo "Android API level must be an integer greater than or equal to 24 (Vulkan baseline)." >&2
  exit 2
fi

case "${output_dir}" in
  ""|/|"${repo_root}"|"${source_dir}"|"${target_dir}")
    echo "Unsafe WGPU_NATIVE_ANDROID_OUTPUT: ${output_dir}" >&2
    exit 2
    ;;
esac

resolve_ndk_root() {
  local candidate=""
  local sdk_root=""
  local best=""

  for candidate in "${ANDROID_NDK_ROOT:-}" "${ANDROID_NDK_HOME:-}"; do
    if [[ -n "${candidate}" && -d "${candidate}/toolchains/llvm/prebuilt" ]]; then
      printf '%s\n' "${candidate}"
      return 0
    fi
  done

  for sdk_root in "${ANDROID_SDK_ROOT:-}" "${ANDROID_HOME:-}"; do
    [[ -n "${sdk_root}" ]] || continue

    best=""
    for candidate in "${sdk_root}"/ndk/*; do
      [[ -d "${candidate}/toolchains/llvm/prebuilt" ]] || continue
      if [[ -z "${best}" || "${candidate##*/}" > "${best##*/}" ]]; then
        best="${candidate}"
      fi
    done
    if [[ -n "${best}" ]]; then
      printf '%s\n' "${best}"
      return 0
    fi

    candidate="${sdk_root}/ndk-bundle"
    if [[ -d "${candidate}/toolchains/llvm/prebuilt" ]]; then
      printf '%s\n' "${candidate}"
      return 0
    fi
  done

  return 1
}

if ! ndk_root="$(resolve_ndk_root)"; then
  echo "Android NDK not found. Set ANDROID_NDK_ROOT to an installed NDK." >&2
  exit 1
fi

host_prebuilt=""
case "$(uname -s)" in
  Darwin)
    for candidate in darwin-arm64 darwin-x86_64; do
      if [[ -d "${ndk_root}/toolchains/llvm/prebuilt/${candidate}" ]]; then
        host_prebuilt="${candidate}"
        break
      fi
    done
    ;;
  Linux)
    for candidate in linux-x86_64 linux-aarch64; do
      if [[ -d "${ndk_root}/toolchains/llvm/prebuilt/${candidate}" ]]; then
        host_prebuilt="${candidate}"
        break
      fi
    done
    ;;
esac

if [[ -z "${host_prebuilt}" ]]; then
  echo "No compatible LLVM toolchain was found in ${ndk_root}." >&2
  exit 1
fi

toolchain="${ndk_root}/toolchains/llvm/prebuilt/${host_prebuilt}"
sysroot="${toolchain}/sysroot"

for required_tool in cargo git rustup; do
  if ! command -v "${required_tool}" >/dev/null 2>&1; then
    echo "Required tool not found: ${required_tool}" >&2
    exit 1
  fi
done

if [[ ! -d "${source_dir}/.git" ]]; then
  git clone --filter=blob:none "${upstream_url}" "${source_dir}"
fi

if [[ -n "$(git -C "${source_dir}" status --porcelain --untracked-files=no)" ]]; then
  echo "Refusing to change a modified external wgpu-native checkout: ${source_dir}" >&2
  exit 1
fi

git -C "${source_dir}" fetch --depth 1 origin "${expected_commit}"
if [[ "$(git -C "${source_dir}" rev-parse HEAD)" != "${expected_commit}" ]]; then
  git -C "${source_dir}" checkout --detach "${expected_commit}"
fi
git -C "${source_dir}" submodule update --init --depth 1 ffi/webgpu-headers

actual_commit="$(git -C "${source_dir}" rev-parse HEAD)"
if [[ "${actual_commit}" != "${expected_commit}" ]]; then
  echo "Expected wgpu-native ${expected_commit}, found ${actual_commit}." >&2
  exit 1
fi

# Each invocation describes exactly the ABIs it was asked to package. Remove
# only the two known generated ABI directories so an earlier optional-emulator
# build cannot leak into a later device-only APK.
rm -rf "${output_dir}/lib/arm64-v8a" "${output_dir}/lib/x86_64"
mkdir -p "${output_dir}/include" "${output_dir}/licenses" "${output_dir}/lib"
install -m 0644 "${source_dir}/ffi/wgpu.h" "${output_dir}/include/wgpu.h"
install -m 0644 "${source_dir}/ffi/webgpu-headers/webgpu.h" "${output_dir}/include/webgpu.h"
install -m 0644 "${source_dir}/LICENSE.APACHE" "${output_dir}/licenses/LICENSE.APACHE"
install -m 0644 "${source_dir}/LICENSE.MIT" "${output_dir}/licenses/LICENSE.MIT"

export CARGO_INCREMENTAL=0
export CARGO_NET_GIT_FETCH_WITH_CLI=true
export CARGO_PROFILE_RELEASE_CODEGEN_UNITS=1
export CARGO_PROFILE_RELEASE_LTO=thin
export CARGO_TARGET_DIR="${target_dir}"
export LC_ALL=C
source_date_epoch="$(git -C "${source_dir}" show -s --format=%ct "${expected_commit}")"
export SOURCE_DATE_EPOCH="${source_date_epoch}"
export TZ=UTC

# Cargo's locked dependency graph plus remapped source paths avoids embedding
# machine-specific checkout locations. lld derives the GNU build-id from the
# linked image instead of using a random identifier.
base_rustflags="--remap-path-prefix=${source_dir}=wgpu-native --remap-path-prefix=${repo_root}=ProGPU -C link-arg=-Wl,--build-id=sha1 -C link-arg=-Wl,-soname,libwgpu_native.so -C link-arg=-Wl,-z,max-page-size=16384 -C link-arg=-Wl,-z,common-page-size=16384"
if [[ -n "${RUSTFLAGS:-}" ]]; then
  export RUSTFLAGS="${RUSTFLAGS} ${base_rustflags}"
else
  export RUSTFLAGS="${base_rustflags}"
fi

built_abis=()
seen_architectures=" "
for architecture in "${requested_architectures[@]}"; do
  if [[ "${seen_architectures}" == *" ${architecture} "* ]]; then
    continue
  fi
  seen_architectures+="${architecture} "

  case "${architecture}" in
    arm64)
      rust_target="aarch64-linux-android"
      clang_target="aarch64-linux-android"
      android_abi="arm64-v8a"
      ;;
    x64)
      rust_target="x86_64-linux-android"
      clang_target="x86_64-linux-android"
      android_abi="x86_64"
      ;;
    *)
      echo "Unsupported architecture: ${architecture}" >&2
      exit 2
      ;;
  esac

  clang="${toolchain}/bin/${clang_target}${android_api_level}-clang"
  llvm_ar="${toolchain}/bin/llvm-ar"
  llvm_nm="${toolchain}/bin/llvm-nm"
  llvm_readelf="${toolchain}/bin/llvm-readelf"
  llvm_strip="${toolchain}/bin/llvm-strip"
  if [[ ! -x "${clang}" || ! -x "${llvm_ar}" || ! -x "${llvm_nm}" ||
        ! -x "${llvm_readelf}" || ! -x "${llvm_strip}" ]]; then
    echo "The selected NDK is missing tools for ${rust_target} at API ${android_api_level}." >&2
    exit 1
  fi

  rustup target add "${rust_target}"

  target_key="$(printf '%s' "${rust_target}" | tr '[:lower:]-' '[:upper:]_')"
  target_env_key="$(printf '%s' "${rust_target}" | tr '-' '_')"
  export "CARGO_TARGET_${target_key}_LINKER=${clang}"
  export "CC_${target_env_key}=${clang}"
  export "AR_${target_env_key}=${llvm_ar}"
  export "CFLAGS_${target_env_key}=--sysroot=${sysroot} -fPIC"
  export "BINDGEN_EXTRA_CLANG_ARGS_${target_env_key}=--target=${clang_target}${android_api_level} --sysroot=${sysroot}"

  # The pinned upstream enables its Vulkan (and, unavoidably for this release,
  # GLES) wgpu-core backend on Android. Only WGSL shader ingestion is enabled;
  # GLSL and SPIR-V input features remain excluded.
  cargo build \
    --manifest-path "${source_dir}/Cargo.toml" \
    --target "${rust_target}" \
    --release \
    --locked \
    --no-default-features \
    --features wgsl

  source_library="${target_dir}/${rust_target}/release/libwgpu_native.so"
  destination_dir="${output_dir}/lib/${android_abi}"
  destination_library="${destination_dir}/libwgpu_native.so"
  if [[ ! -f "${source_library}" ]]; then
    echo "Cargo completed without producing ${source_library}." >&2
    exit 1
  fi

  mkdir -p "${destination_dir}"
  install -m 0755 "${source_library}" "${destination_library}"
  "${llvm_strip}" --strip-unneeded "${destination_library}"

  case "${architecture}" in
    arm64) expected_machine="AArch64" ;;
    x64) expected_machine="Advanced Micro Devices X86-64" ;;
  esac
  if ! "${llvm_readelf}" -h "${destination_library}" |
      awk -v expected="${expected_machine}" '
        /^[[:space:]]*Machine:/ {
          sub(/^[[:space:]]*Machine:[[:space:]]*/, "", $0)
          found = ($0 == expected)
        }
        END { exit !found }
      '; then
    echo "Packaged ${android_abi} library has the wrong ELF machine type." >&2
    exit 1
  fi
  if ! "${llvm_readelf}" -d "${destination_library}" |
      awk '/\(SONAME\)/ && /\[libwgpu_native\.so\]/ { found = 1 } END { exit !found }'; then
    echo "Packaged ${android_abi} library has no libwgpu_native.so SONAME." >&2
    exit 1
  fi
  if ! "${llvm_nm}" -D --defined-only "${destination_library}" |
      awk '$NF == "wgpuCreateInstance" { found = 1 } END { exit !found }'; then
    echo "Packaged ${android_abi} library does not export the WebGPU C ABI." >&2
    exit 1
  fi
  if ! "${llvm_readelf}" -l "${destination_library}" |
      awk '$1 == "LOAD" { found = 1; if ($NF != "0x4000") bad = 1 } END { exit !found || bad }'; then
    echo "Packaged ${android_abi} library is not aligned for Android 16 KiB pages." >&2
    exit 1
  fi
  built_abis+=("${android_abi}")
done

ndk_revision="unknown"
if [[ -f "${ndk_root}/source.properties" ]]; then
  ndk_revision="$(sed -n 's/^Pkg\.Revision[[:space:]]*=[[:space:]]*//p' "${ndk_root}/source.properties" | head -n 1)"
fi
rust_version="$(rustc --version)"
{
  printf 'wgpu-native-commit=%s\n' "${expected_commit}"
  printf 'silk-net-webgpu-abi=2.23.0\n'
  printf 'android-api-level=%s\n' "${android_api_level}"
  printf 'android-abis=%s\n' "$(IFS=,; printf '%s' "${built_abis[*]}")"
  printf 'cargo-features=wgsl\n'
  printf 'runtime-backend=vulkan\n'
  printf 'ndk-revision=%s\n' "${ndk_revision}"
  printf 'rust-version=%s\n' "${rust_version}"
} > "${output_dir}/BUILD-MANIFEST.txt"

checksum_file="${output_dir}/SHA256SUMS"
checksum_temp="${checksum_file}.tmp"
: > "${checksum_temp}"
while IFS= read -r relative_path; do
  if command -v sha256sum >/dev/null 2>&1; then
    digest="$(sha256sum "${output_dir}/${relative_path}" | awk '{print $1}')"
  else
    digest="$(shasum -a 256 "${output_dir}/${relative_path}" | awk '{print $1}')"
  fi
  printf '%s  %s\n' "${digest}" "${relative_path}" >> "${checksum_temp}"
done < <(
  cd "${output_dir}"
  find BUILD-MANIFEST.txt include licenses lib -type f -print | LC_ALL=C sort
)
mv "${checksum_temp}" "${checksum_file}"

echo "Created Android wgpu-native package at ${output_dir} from ${expected_commit}."
echo "ABIs: $(IFS=,; printf '%s' "${built_abis[*]}") (API ${android_api_level})"
