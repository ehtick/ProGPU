#!/usr/bin/env bash

# shellcheck disable=SC2034 # This file is sourced by the pack and publish scripts.
progpu_avalonia_package_ids=(
  "ProGPU.Avalonia.Rendering"
  "ProGPU.Avalonia.SilkNet"
  "ProGPU.Avalonia.Rendering"
  "ProGPU.Avalonia.SilkNet"
)

progpu_avalonia_package_projects=(
  "src/ProGPU.Avalonia.Rendering/Avalonia.ProGpu.csproj"
  "src/ProGPU.Avalonia.SilkNet/Avalonia.SilkNet.csproj"
  "src/ProGPU.Avalonia.Rendering.V11/Avalonia.ProGpu.csproj"
  "src/ProGPU.Avalonia.SilkNet.V11/Avalonia.SilkNet.csproj"
)

progpu_avalonia_package_versions=(
  "12.0.5-preview.27"
  "12.0.5-preview.27"
  "11.3.18-preview.27"
  "11.3.18-preview.27"
)

if [[ "${#progpu_avalonia_package_ids[@]}" -ne "${#progpu_avalonia_package_projects[@]}" ||
      "${#progpu_avalonia_package_ids[@]}" -ne "${#progpu_avalonia_package_versions[@]}" ]]; then
  echo "ProGPU Avalonia package list arrays must have the same length." >&2
  exit 1
fi
