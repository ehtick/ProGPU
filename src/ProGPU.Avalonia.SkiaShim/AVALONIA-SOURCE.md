# Avalonia Skia source provenance

`Avalonia.Skia.csproj` compiles the Avalonia backend from the pinned fork
submodule:

- repository: <https://github.com/wieslawsoltes/Avalonia>
- tag: `progpu-avalonia-v12.0.5-preview.19`
- commit: `5378af03f17a4d9d2845882229ffed7f67350037`
- linked directory: `external/Avalonia/src/Skia/Avalonia.Skia`

That tag changes `GlyphRunImpl.cs` relative to the Avalonia 12.0.5 release.
The local `GlyphRunImpl.cs` override deliberately restores the original file
from:

- repository: <https://github.com/AvaloniaUI/Avalonia>
- tag: `12.0.5`
- commit: `fee9c561ce036e8a3e8cee2397c75ca599b4790d`

The other 53 linked C# files are already byte-identical to that release. The
effective 54-file source set therefore remains unmodified Avalonia 12.0.5.
Its deterministic SHA-256 over the sorted relative paths, a zero byte after
each path, and each file's bytes is:

`b449fb8ed977fcafa9ebc006f0a38f9229d7f78ce4a1986ceccc8fd1cbaf2d2f`

The live WebGPU presentation bridge is implemented in `ProGPU.Backend`,
`ProGPU.Avalonia.SilkNet`, and the ProGPU SkiaSharp shim. No ProGPU-specific
rendering changes are made to the effective Avalonia backend source.

The upstream MIT license is reproduced in `AVALONIA-LICENSE.md`.
