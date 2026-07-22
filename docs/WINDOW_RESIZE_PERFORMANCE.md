# Window resize performance

## Outcome

The desktop resize path now coalesces GLFW logical/framebuffer event storms into the next
normal render tick. A framebuffer callback publishes invalidation only; it no longer performs
a nested swapchain configuration, full layout/scene compilation, GPU submission, and present.
The render tick reads the latest framebuffer size, configures that size once, and renders it.

The accompanying retained multi-column text path separates width-independent shaping inputs
from width-dependent line breaking. It retains those inputs per document block, recycles
positioned-character objects, represents candidate lines as index ranges instead of copying
`RichChar` structs, and skips a duplicate arrange layout when measure already used the final
size. Rasterization, DPI/subpixel snapping, glyph/path atlas keys, and shaders are unchanged.

## Matched Release measurements

Measurements use the `Text & Documents` sample, 120 warm-up frames, 300 measured frames,
VSync enabled, a repeated 800x600 to 1280x800 resize sweep, and memory counters enabled on
macOS/Metal. Both runs used the same final Release application. CPU allocation attribution
used EventPipe randomized allocation sampling; exact allocation and GC values come from the
runtime counters. Metal System Trace was captured for both runs.

| Metric | Before | After | Change |
|---|---:|---:|---:|
| Managed allocation / measured frame | 1,761,326 B | 421,501 B | **-76.1%** |
| Gen0 collections | 58 | 15 | **-74.1%** |
| GC pause | 36.53 ms | 10.69 ms | **-70.7%** |
| Resize callback average | 8.0714 ms | 0.0002 ms | **-99.998%** |
| Resize callback maximum | 52.8272 ms | 0.0040 ms | **-99.992%** |
| Layout / frame | 2.4024 ms | 2.2285 ms | **-7.2%** |
| Compositor / frame | 1.3967 ms | 1.4029 ms | within trace noise |
| Physical footprint | 496.19 MB | 476.86 MB | **-3.9%** |
| Lifetime max physical footprint | 554.60 MB | 540.77 MB | **-2.5%** |

The old measurement contained 150 framebuffer callbacks and 149 nested frames among 300
reported frames, so only about half of those frames were normal application-loop updates.
After coalescing, 299 of 300 measured frames consumed a new resize request through the normal
tick. This restores the display's update cadence instead of using the second available Metal
drawable from inside the callback. Framebuffer callbacks perform no uploads or submissions.

GPU residency and quality-sensitive work remained stable: the glyph atlas stayed at
1024x2560, the color atlas at 256x512, the path atlas at 512x2048 with 134 entries, and the
post-trace recorded 58 glyph-outline writes and 47 compute batches. No atlas clear, eviction,
shader, raster scale, or subpixel policy changed. Focused rich-text, bidi, flow-direction,
and table tests cover the line-range refactor; a new regression verifies that identical
measure/arrange sizes perform one multi-column layout. The browser Release AOT host is also
resized through multiple viewport sizes to verify its independent canvas configuration path.

## Cross-engine design record

This is a clean-room implementation. External sources informed architecture and observable
contracts only; no source was copied, translated, or structurally reproduced.

| Engine / primary source | Relevant production behavior | ProGPU decision |
|---|---|---|
| [Vello winit example](https://github.com/linebender/vello/blob/main/examples/with_winit/src/lib.rs) | `Resized` updates the surface and requests redraw; actual rendering remains in `RedrawRequested`. Outdated/suboptimal surfaces are reconfigured and redrawn. | Adopt event/render separation and latest-size rendering; adapt it to ProGPU's continuously ticking Silk host. |
| [Direct2D HWND render target](https://learn.microsoft.com/en-us/windows/win32/api/d2d1/nn-d2d1-id2d1hwndrendertarget), [Direct2D quickstart](https://learn.microsoft.com/en-us/windows/win32/direct2d/direct2d-quickstart), [DXGI `ResizeBuffers`](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgiswapchain-resizebuffers) | Retain the render target, resize its backing buffers for the new client size, and draw through the normal paint path after releasing outstanding references. | Retain device/pipelines/atlases, configure only the latest physical size, and keep rendering out of the native callback. |
| [Win2D without controls](https://microsoft.github.io/Win2D/WinUI3/html/WithoutControls.htm), [CanvasSwapChain.ResizeBuffers](https://microsoft.github.io/Win2D/WinUI3/html/M_Microsoft_Graphics_Canvas_CanvasSwapChain_ResizeBuffers_2.htm) | Size changes update swapchain buffers while draw/present remain distinct operations. | Adopt the same ownership split; reject nested present as a resize-notification side effect. |
| [WebRender rendering overview](https://firefox-source-docs.mozilla.org/gfx/RenderingOverview.html), [Firefox vsync pipeline](https://firefox-source-docs.mozilla.org/gfx/Silk.html) | A retained scene is culled into frames, caches localize changed work, and compositor/painting are aligned to hardware vsync. | Preserve compiled-scene and atlas reuse and align resize work with the normal display tick. Full damage tiling is outside this fix. |
| [Skia shaped-text design](https://docs.skia.org/docs/dev/design/text_shaper/), [SkParagraph API](https://skia.googlesource.com/skia/+/refs/heads/main/modules/skparagraph/include/Paragraph.h) | Shaping and width-dependent formatting are separable retained stages; paragraph objects can be laid out again at a new width. | Retain block shaping inputs and redo only line/column placement when width changes. |
| [DirectWrite `IDWriteTextLayout`](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/nn-dwrite-idwritetextlayout), [`SetMaxWidth`](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/nf-dwrite-idwritetextlayout-setmaxwidth) | A fully analyzed/formatted text block is retained while layout constraints can change. | Adapt the retained-layout contract per presenter without sharing viewport coordinates between controls. |
| [Parley layout](https://docs.rs/parley/latest/parley/), [Parley retained layout data](https://docs.rs/parley/latest/src/parley/layout/data.rs.html) | Shaped runs/clusters/glyphs are stored separately from lines; re-linebreaking and alignment do not require reconstructing text styles. | Retain block character/font/style resolution and paragraph metrics; compute new column breaks from index ranges. |
| [HarfBuzz shaping plans and caching](https://harfbuzz.github.io/shaping-plans-and-caching.html) | Stable face/script/language/feature work is reusable, while shaping results still obey Unicode/OpenType context. | Preserve CPU shaping and line-boundary reshaping for correctness; reject a GPU-only shaper or width-keyed global cache. |

Across these engines, startup remains lazy, shaping/layout and retained scene data survive
unrelated frames, visibility controls demand, uploads are demand-driven, and GPU work is
batched at the render boundary. ProGPU keeps those existing contracts. Surface capability
selection is now cached for a surface lifetime; a lost/outdated surface explicitly refreshes
capabilities. Font fallback, variable-font instances, DPI/subpixel snapping, atlas generation,
and device-loss invalidation remain part of their existing typed cache keys.

Rejected alternatives were rendering synchronously in the resize callback, adding a timer
debounce that would lag behind the pointer, drawing a lower-resolution stretched frame,
dropping text features, lowering coverage quality, reshaping unchanged text globally, or
pre-uploading hidden content. Each either increases latency/memory or violates quality and
invalidation contracts.

## Reproduction

```bash
PROGPU_SAMPLE_BENCHMARK_PAGE='Text & Documents' \
PROGPU_SAMPLE_BENCHMARK_WARMUP_FRAMES=120 \
PROGPU_SAMPLE_BENCHMARK_MEASURE_FRAMES=300 \
PROGPU_SAMPLE_BENCHMARK_RESIZE=true \
PROGPU_SAMPLE_BENCHMARK_MEMORY=true \
PROGPU_SAMPLE_BENCHMARK_VSYNC=true \
src/ProGPU.Samples.Desktop/bin/Release/net10.0/ProGPU.Samples.Desktop
```

Set `PROGPU_SAMPLE_BENCHMARK_RESIZE_MIN_WIDTH`, `_MIN_HEIGHT`, `_MAX_WIDTH`,
`_MAX_HEIGHT`, and `_STEP` to change the deterministic sweep.
