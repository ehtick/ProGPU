# ProGPU rendering and Silk.NET windowing

The ProGPU integration ships the same two package IDs in separate Avalonia 12 and
Avalonia 11 version lanes:

| Avalonia lane | Package | Assembly | Purpose |
| --- | --- | --- | --- |
| 12.0.5 | `ProGPU.Avalonia.Rendering` `12.0.5-preview.26` | `Avalonia.ProGpu` | ProGPU/WebGPU rendering backend |
| 12.0.5 | `ProGPU.Avalonia.SilkNet` `12.0.5-preview.26` | `Avalonia.SilkNet` | Cross-platform Silk.NET windowing backend |
| 11.3.18 | `ProGPU.Avalonia.Rendering` `11.3.18-preview.26` | `Avalonia.ProGpu` | Shared-source ProGPU/WebGPU rendering backend |
| 11.3.18 | `ProGPU.Avalonia.SilkNet` `11.3.18-preview.26` | `Avalonia.SilkNet` | Shared-source Silk.NET windowing backend |

The Avalonia 12 packages are built against exactly Avalonia `12.0.5`; the
Avalonia 11 packages are built against exactly Avalonia `11.3.18`. Both lanes
use ProGPU `0.1.0-preview.26`. They intentionally use `ProGPU.*` package IDs;
no `Avalonia.*` package ID is produced.

The NuGet package page uses `docs/progpu-package-readme.md`. Keep its install, startup, API lease, and troubleshooting instructions current when package contracts change.

The original package artwork is maintained as `build/Assets/ProGpuAvaloniaIcon.svg` and rendered to `build/Assets/ProGpuAvaloniaIcon.png`. NuGet uses the PNG, and both files are included in each integration package.

## Development projects

All four integration projects live in this repository and reference the local
ProGPU runtime projects. Build the Avalonia 12 lane with:

```bash
dotnet build src/ProGPU.Avalonia.Rendering/Avalonia.ProGpu.csproj
dotnet build src/ProGPU.Avalonia.SilkNet/Avalonia.SilkNet.csproj
```

Build the Avalonia 11 lane with:

```bash
dotnet build src/ProGPU.Avalonia.Rendering.V11/Avalonia.ProGpu.csproj
dotnet build src/ProGPU.Avalonia.SilkNet.V11/Avalonia.SilkNet.csproj
```

The v11 projects contain no duplicated backend implementation. They source-link
the Avalonia 12 project sources and define `AVALONIA11`; conditionals are limited
to concrete Avalonia API differences. Warning `AVA3001` is expected because the
backends implement Avalonia platform interfaces that are intentionally private.
The package dependencies are exact pins, so upgrading Avalonia requires a
matching integration build.

## Control Catalog defaults

The desktop Control Catalog starts with Silk.NET windowing and ProGPU rendering when no renderer argument is supplied:

```bash
dotnet run --project samples/ControlCatalog.Desktop/ControlCatalog.Desktop.csproj
```

Pass `--skiashim` to opt into the source-integrated Avalonia Skia backend running on
the ProGPU SkiaSharp shim.

## Pack locally

Pack the ProGPU `0.1.0-preview.26` portable runtime packages first, then pack
both integration lanes:

```bash
PROGPU_PACKAGE_GROUP=portable ./eng/progpu-pack.sh
./scripts/progpu-pack.sh
```

The package-only application can perform that sequence and then restore in an
isolated package cache:

```bash
PROGPU_INTEGRATION_BUILD_ONLY=1 \
  ./integration/ProGpuPackageApp/run.sh local
```

Set `PROGPU_PACKAGE_SOURCE` to use another absolute local package directory. The
expected integration output is eight files: a `.nupkg` and `.snupkg` for each of
the four package/version entries.

## Publish to NuGet.org

Keep the API key out of command history and repository files. From Bash:

```bash
read -rsp "NuGet API key: " NUGET_API_KEY && printf '\n'
export NUGET_API_KEY
./scripts/progpu-publish.sh
unset NUGET_API_KEY
```

`progpu-publish.sh` repacks, validates all expected artifacts, and pushes each package with `--skip-duplicate`; `dotnet nuget push` discovers and uploads the matching symbol package automatically. Override `NUGET_SOURCE` only when publishing to another NuGet-compatible server.

Release order:

1. Tag and publish ProGPU `0.1.0-preview.26`.
2. Confirm the required ProGPU packages are available from NuGet.org.
3. Pack and test both Avalonia integration lanes.
4. Publish the two `12.0.5-preview.26` packages and the two
   `11.3.18-preview.26` packages.

## Consume the packages

```xml
<ItemGroup>
  <PackageReference Include="Avalonia" Version="12.0.5" />
  <PackageReference Include="Avalonia.Fonts.Inter" Version="12.0.5" />
  <PackageReference Include="Avalonia.HarfBuzz" Version="12.0.5" />
  <PackageReference Include="ProGPU.Avalonia.Rendering" Version="12.0.5-preview.26" />
  <PackageReference Include="ProGPU.Avalonia.SilkNet" Version="12.0.5-preview.26" />
</ItemGroup>
```

Configure both backends before starting the desktop lifetime:

```csharp
using Avalonia.Rendering.Composition;

public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UseSilkNet()
        .UseProGpu()
        .With(new CompositionOptions
        {
            UseRegionDirtyRectClipping = false
        })
        .UseHarfBuzz()
        .WithInterFont();
```

`UseSkia()` remains available as a compatibility alias for the ProGPU renderer, but `UseProGpu()` avoids ambiguity with Avalonia's Skia package.

Use `IProGpuApiLeaseFeature` from `ICustomDrawOperation.Render` for scoped access to the ProGPU scene command recorder and active `WgpuContext`. The complete vector drawing and WGSL ShaderToy examples, plus the lease lifetime rules, are in `docs/progpu-package-readme.md` and `integration/ProGpuPackageApp/ProGpuLeaseView.cs`.

For Avalonia 11 applications, use the same package IDs with
`11.3.18-preview.26` and pin the Avalonia packages to `11.3.18`.

The cross-engine design review and validation evidence are recorded in
`docs/progpu-avalonia-rendering-research.md`.
