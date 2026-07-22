# GPU text coverage-cache architecture

Date: 2026-07-22

## Objective and constraints

This design reduces persistent GPU memory for ProGPU text and vector coverage without changing the retained-scene, shaping, fallback-font, variable-font, DPI, subpixel, hinting, or winding contracts. Rasterization remains GPU-only: no coverage image is rasterized on, or read back through, the CPU.

The implementation is clean-room. The sources below informed public behavior, architecture, and tradeoffs only; no foreign source code, helper organization, names, or encoded tables were copied or translated.

## Primary-source research

| Engine | Relevant architecture | ProGPU decision |
| --- | --- | --- |
| [Skia strike cache](https://skia.googlesource.com/skia/+/main/src/core/SkStrikeCache.h), [SkParagraph API](https://skia.googlesource.com/skia/+/refs/heads/main/modules/skparagraph/include/Paragraph.h), and [SkParagraph LRU cache](https://skia.googlesource.com/skia/+/a1f873e79a50/modules/skparagraph/include/ParagraphCache.h) | Separates reusable font/strike data, bounds caches by bytes/count, retains shaped paragraph state, and treats glyph representations as cacheable resources rather than one unbounded global image. | Adopt byte-accounted resource limits, representation separation, and retained layout reuse. Keep ProGPU's typed font/outline ownership and GPU rasterizer instead of adopting Skia's CPU strike implementation. |
| [DirectWrite glyph-run analysis](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/nn-dwriteglyphrunanalysis) and [Direct2D text performance guidance](https://learn.microsoft.com/en-us/windows/win32/direct2d/improving-direct2d-performance) | Exposes bounded glyph-run alpha bounds and uses one byte per pixel for grayscale alpha, three for ClearType; encourages reuse of text layouts and rendering resources. | Adopt compact grayscale coverage and retained shaped-run reuse. Preserve ProGPU's existing shader reconstruction of grayscale/ClearType output and four-way physical-pixel snapping. |
| [DirectWrite color fonts](https://learn.microsoft.com/en-us/windows/win32/directwrite/color-fonts), [Win2D overview](https://learn.microsoft.com/en-us/windows/apps/develop/win2d/), and [Win2D `CanvasTextLayout`](https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Text_CanvasTextLayout.htm) | Color glyph layers are a distinct representation, while reusable text layouts retain shaping and line-layout work. Win2D exposes Direct2D/DirectWrite through a GPU-accelerated API. | Split RGBA bitmap/color glyphs from monochrome coverage. Keep shaping/layout above the atlas and preserve fallback/variable-font state in existing typed cache keys. |
| [WebRender rendering overview](https://searchfox.org/mozilla-central/source/gfx/docs/RenderingOverview.rst) and [glyph rasterizer](https://searchfox.org/mozilla-central/source/gfx/wr/wr_glyph_rasterizer/src/lib.rs) | Culls a retained scene before preparing visible resources, distinguishes alpha and color bitmap glyph formats, and manages them through a texture cache. Glyph raster preparation can run on workers. | Adopt alpha/color format separation and demand-driven population after visibility/scene compilation. Retain ProGPU's GPU compute preparation rather than adding CPU raster workers. |
| [Vello](https://github.com/linebender/vello) and its [glyph rendering design discussion](https://github.com/linebender/vello/issues/204) | Uses GPU compute for vector rendering and evaluates dynamic outlines versus cached glyph images; transform-specific hinting makes one universal cached representation unsuitable. | Keep analytic GPU rasterization and bounded cached coverage. Reject an all-vector-per-frame design because stable UI text benefits from retained coverage, and reject SDF/MSDF substitution because it would change small-text hinting and coverage quality. |
| [Parley](https://docs.rs/parley/latest/parley/) | Shares font and layout contexts and keeps shaping/layout reusable and independent of the final renderer. | Preserve ProGPU's reusable CPU shaping/layout results and glyph indices; do not move Unicode/OpenType shaping into the coverage cache. |
| [HarfBuzz shaping-plan caching](https://harfbuzz.github.io/shaping-plans-and-caching.html) | Reuses shaping plans for matching face, segment properties, and features. | Preserve current shaped results, OpenType feature state, fallback selection, and variable-font instance identity; atlas changes start only after glyph identity and position are known. |
| [WebGPU specification](https://gpuweb.github.io/gpuweb/) | Defines `r8unorm`, storage buffers, and buffer-to-texture copies with aligned row layout. Storage-texture format capabilities vary by implementation tier. | Use a universally writable storage buffer as compute output, pack four coverage bytes per `u32`, then issue GPU buffer-to-`r8unorm` copies. This avoids depending on optional R8 storage-texture support. |

The required comparison areas map as follows:

- Startup and lazy initialization: atlas textures and pipelines retain the current lazy ownership; no font enumeration, outline upload, or raster work is added at startup.
- Shaping and layout reuse: unchanged. Positioned glyph indices and advances remain reusable CPU results, following the separation used by DirectWrite, Parley, HarfBuzz, and SkParagraph-style stacks.
- Retained scene and visibility: unchanged. Only glyphs and paths demanded by compiled visible commands enter the atlas, comparable to WebRender's retained-scene resource preparation.
- Cache keys and eviction: all existing glyph style, physical size, DPI/subpixel, local-transform, variable-font, path-phase, and generation keys remain intact. Alpha and color entries cannot reuse one another's storage.
- Demand-driven upload: compute output is copied only for pending glyph/path rectangles. There is no full-atlas upload or CPU readback.
- Worker preparation: font parsing/shaping remains reusable CPU work. Coverage preparation is GPU compute, batched before rendering; adding CPU raster workers was rejected.
- GPU batching: glyphs share a persistent bounded ring; paths share a compact per-batch output buffer. One invocation produces four adjacent coverage texels.
- DPI, subpixel, and hinting: raster dimensions, physical-space phase keys, final unsnapped placement, sample grids, gamma, and ClearType reconstruction are unchanged.
- Fallback and variable fonts: existing font identity, shaped glyph index, and outline selection remain the authoritative inputs. The storage format does not merge font instances.
- Device loss and atlas generations: resources remain owned by the compositor/context; atlas `Generation` changes only on real clear/repack. A format split does not weaken retained-scene invalidation or capacity-retry behavior.

## Implemented design

### Compact GPU coverage pipeline

The glyph and path compute shaders keep their existing analytic winding and sample grids. Each invocation now rasterizes four adjacent output pixels and packs the four normalized coverage bytes into one `u32` in a storage buffer. A command-encoder buffer-to-texture copy places the result in an `R8Unorm` atlas. Row pitch is aligned to WebGPU's 256-byte copy requirement.

For a raster rectangle of width `W`, height `H`, segment count `S`, and sample grid `A`, raster work remains `O(W * H * A * S)` in the worst case. Output storage is `O(align256(W) * H)` bytes rather than `O(4 * atlasWidth * atlasHeight)` writable RGBA storage. The output never crosses the CPU boundary.

### Representation-separated atlases

- Monochrome glyph coverage: `2560 x 2560 x 1` byte R8 texture.
- Bitmap/color glyphs: independently packed `512 x 512 x 4` byte RGBA texture.
- Paths and vector-glyph fallback: `2048 x 2048 x 1` byte R8 texture.
- Glyph compute staging: bounded 2 MiB persistent GPU ring.
- Path compute staging: batch-sized GPU buffer, released after submitted work completes.

The text shader selects the R8 or RGBA binding from the retained glyph representation flag. This avoids scaling the color allocation to the dimensions required by Latin/CJK/vector coverage while preserving premultiplied bitmap-glyph sampling.

### Bounded outline storage

GPU glyph outline buffers grow by GPU-to-GPU copy and no longer retain duplicate, permanently growing managed record/segment lists per font. A reusable CPU scratch list is used only while parsing a newly demanded outline. Allocated GPU outline capacity is measured and bounded to 4 MiB by default; the cache is rebuilt lazily after a frame-boundary trim. Coverage already resident in the atlas remains valid.

### Protocol and diagnostics

The typed native and browser WebGPU APIs now expose command-encoder buffer-to-texture copies. Compositor metrics report coverage texture bytes, color texture bytes, glyph staging bytes, outline GPU bytes, path texture bytes, current path staging, and peak path staging so memory regressions are observable rather than inferred from process RSS.

## Memory and performance evidence

The sample configuration previously reserved two RGBA atlases:

| Persistent/bounded GPU allocation | Before | After |
| --- | ---: | ---: |
| Glyph coverage/color textures | 26,214,400 B | 7,602,176 B |
| Path coverage texture | 16,777,216 B | 4,194,304 B |
| Bounded glyph staging | 0 B | 2,097,152 B |
| **Fixed atlas + glyph staging total** | **42,991,616 B** | **13,893,632 B** |

This is a 67.7% reduction (29,097,984 bytes) in the exact fixed/bounded allocation represented by those resources. The new system additionally reports, and bounds, outline and transient path staging rather than hiding them in process-level memory figures.

A release-build sweep visited all text/font sample pages (Text & Documents, Rich Document Editor, Markdown Playground, Glyph Run Showcase, Text Shaping Lab, Typography & Scripts, Inter Typeface, Interactive Input, Font Glyph Browser, WPF Shim Showcase, and SkiaSharp Shim) before measuring the same Data Virtualization scene for 60 warm-up and 180 measured frames:

| Metric | Baseline | New design | Change |
| --- | ---: | ---: | ---: |
| Compiled-scene CPU time | 0.9577 ms | 0.5308 ms | -44.6% |
| Compositor CPU time | 1.2040 ms | 0.7687 ms | -36.2% |
| Managed allocation/frame | 26,953 B | 25,964 B | -3.7% |
| Wall throughput | 480.26 FPS | 472.54 FPS | -1.6% |
| Glyph/path capacity failures | 0 / 0 | 0 / 0 | unchanged |
| Glyph evictions / atlas clears / path resets | 0 / 0 / 0 | 0 / 0 / 0 | unchanged |

Wall throughput varied by 1.6%, so it is treated as neutral noise rather than an improvement claim. Process RSS and OS memory-footprint counters were also noisy and contradictory between isolated runs; the memory claim therefore uses exact resource sizes exposed by compositor metrics. The populated new run held 2,712 glyph entries and 141 path entries, allocated 1,775,616 bytes of GPU outline capacity, used 196,080 bytes of path CPU cache, and peaked at 1,178,624 bytes of transient path staging.

An isolated Font Glyph Browser run also reduced compilation from 3.5679 to 1.7468 ms and compositor CPU time from 3.9711 to 2.0189 ms, while allocations fell from 117,561 to 102,670 bytes/frame. These timings support the architectural result but are not substituted for the all-feature sweep.

## Quality and regression evidence

- The glyph shader retains 8x8 high-precision coverage and the direction-aware half-open quadratic/cubic winding rules.
- The path shader retains each command's selected sample grid, fill rule, transform phase, scale quantization, and recovery behavior.
- Color bitmap glyphs retain RGBA data and filtered sampling in a dedicated texture.
- The text shader retains aliased, grayscale, gamma/contrast, mask, and ClearType paths; only the sampled texture binding changes.
- Atlas generations still change only when cached UV contents are cleared, moved, or repacked.
- Regression tests cover exact configured residency, R8 readback, color/coverage separation, shader resource contracts, atlas recovery, phase bounds, and existing rendering behavior.

Validation command: `dotnet test src/ProGPU.Tests/ProGPU.Tests.csproj -c Release --no-restore`. Result: 2,255 passed, 0 failed.

## Rejected alternatives

- RGBA storage textures for coverage: portable and simple, but waste three channels for every monochrome texel.
- R8 storage-texture writes: direct, but not a sufficiently portable baseline across the native and browser targets.
- CPU rasterization or CPU repacking: lowers GPU requirements but violates the GPU-only goal and adds transfer/synchronization cost.
- SDF/MSDF atlases: attractive for scale reuse, but change small-text coverage, hinting, stroke, and transformed vector quality.
- Per-frame uncached vector text: bounds residency but repeats analytic raster work for stable UI text.
- One universal color/coverage atlas: recreates the RGBA tax and couples unrelated capacity/eviction pressure.
