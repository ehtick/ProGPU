#!/usr/bin/env bash
set -euo pipefail

# Builds the exact wgpu-native ABI consumed by Silk.NET.WebGPU 2.23.0.
# Source remains an external build input under artifacts/ and is never vendored into ProGPU.

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_dir="${WGPU_NATIVE_SOURCE:-${repo_root}/artifacts/wgpu-native-src}"
output_dir="${WGPU_NATIVE_OUTPUT:-${repo_root}/artifacts/wgpu-native-ios}"
# Silk.NET 2.23 still exposes the WebGPU C ABI used by wgpu-native's
# May 2024 header update (with wgpu-core 0.19.4).
# Do not replace this with Silk's newer wgpu-native submodule pointer: its callback-info
# and surface-source ABI is incompatible with the generated Silk.NET.WebGPU assembly.
expected_commit="33133da4ec5a0174cb21539ef2d3346f75200411"
upstream_url="https://github.com/gfx-rs/wgpu-native.git"

if [[ ! -d "${source_dir}/.git" ]]; then
  git clone --filter=blob:none "${upstream_url}" "${source_dir}"
fi

git -C "${source_dir}" fetch --depth 1 origin "${expected_commit}"
git -C "${source_dir}" checkout --detach "${expected_commit}"
git -C "${source_dir}" submodule update --init --depth 1 ffi/webgpu-headers

actual_commit="$(git -C "${source_dir}" rev-parse HEAD)"
if [[ "${actual_commit}" != "${expected_commit}" ]]; then
  echo "Expected wgpu-native ${expected_commit}, found ${actual_commit}." >&2
  exit 1
fi

rustup target add aarch64-apple-ios aarch64-apple-ios-sim

export IPHONEOS_DEPLOYMENT_TARGET="${IPHONEOS_DEPLOYMENT_TARGET:-15.0}"
export CARGO_PROFILE_RELEASE_LTO="thin"
export CARGO_PROFILE_RELEASE_CODEGEN_UNITS="1"

features="wgsl,metal"
cargo build \
  --manifest-path "${source_dir}/Cargo.toml" \
  --target aarch64-apple-ios \
  --release \
  --locked \
  --no-default-features \
  --features "${features}"
cargo build \
  --manifest-path "${source_dir}/Cargo.toml" \
  --target aarch64-apple-ios-sim \
  --release \
  --locked \
  --no-default-features \
  --features "${features}"

mkdir -p "${output_dir}/include"
cp "${source_dir}/ffi/wgpu.h" "${output_dir}/include/wgpu.h"
cp "${source_dir}/ffi/webgpu-headers/webgpu.h" "${output_dir}/include/webgpu.h"

# Silk resolves entry points lazily. iOS static libraries have no dlopen-able image, so
# retain the C API and expose a single resolver that returns direct function pointers.
resolver_dir="${output_dir}/resolver"
mkdir -p "${resolver_dir}"
resolver_source="${resolver_dir}/progpu_wgpu_resolver.c"
{
  echo '#include <stddef.h>'
  echo '#include <string.h>'
  echo '#include "wgpu.h"'
  echo 'void* progpu_wgpu_get_proc_address(const char* proc) {'
  echo '  if (proc == NULL) return NULL;'
  grep -hEo 'wgpu[A-Z][A-Za-z0-9_]+[[:space:]]*\(' \
    "${source_dir}/ffi/webgpu-headers/webgpu.h" \
    "${source_dir}/ffi/wgpu.h" | sed -E 's/[[:space:]]*\($//' | sort -u | while IFS= read -r symbol; do
      echo "  if (strcmp(proc, \"${symbol}\") == 0) return (void*)&${symbol};"
    done
  echo '  return NULL;'
  echo '}'
} > "${resolver_source}"

device_sdk="$(xcrun --sdk iphoneos --show-sdk-path)"
simulator_sdk="$(xcrun --sdk iphonesimulator --show-sdk-path)"
clang -arch arm64 -isysroot "${device_sdk}" -miphoneos-version-min="${IPHONEOS_DEPLOYMENT_TARGET}" \
  -I"${output_dir}/include" -c "${resolver_source}" -o "${resolver_dir}/resolver-ios-arm64.o"
clang -arch arm64 -isysroot "${simulator_sdk}" -mios-simulator-version-min="${IPHONEOS_DEPLOYMENT_TARGET}" \
  -I"${output_dir}/include" -c "${resolver_source}" -o "${resolver_dir}/resolver-iossimulator-arm64.o"

libtool -static -o "${resolver_dir}/libwgpu_native-ios-arm64.a" \
  "${resolver_dir}/resolver-ios-arm64.o" \
  "${source_dir}/target/aarch64-apple-ios/release/libwgpu_native.a"
libtool -static -o "${resolver_dir}/libwgpu_native-iossimulator-arm64.a" \
  "${resolver_dir}/resolver-iossimulator-arm64.o" \
  "${source_dir}/target/aarch64-apple-ios-sim/release/libwgpu_native.a"

xcframework="${output_dir}/wgpu_native.xcframework"
if [[ -e "${xcframework}" ]]; then
  rm -rf "${xcframework}"
fi

xcodebuild -create-xcframework \
  -library "${resolver_dir}/libwgpu_native-ios-arm64.a" \
  -headers "${output_dir}/include" \
  -library "${resolver_dir}/libwgpu_native-iossimulator-arm64.a" \
  -headers "${output_dir}/include" \
  -output "${xcframework}"

echo "Created ${xcframework} from wgpu-native ${expected_commit}."
