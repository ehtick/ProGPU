# Touch, Gestures, Mobile Input, and Responsive UI Plan

## Goal

Provide one WinUI-aligned input and adaptive-layout surface for ProGPU applications on
desktop, embedded hosts, and browser/mobile. Platform hosts translate native events
into neutral pointer and text-edit records; the retained WinUI layer owns hit testing,
routing, capture, recognition, focus, control behavior, and invalidation.

## Compatibility target

The public surface follows the current WinUI concepts and names where ProGPU already
owns the corresponding namespace:

- unified `Pointer`, `PointerPoint`, `PointerPointProperties`, stable `PointerId`, and
  `PointerDeviceType` data on every routed pointer event;
- per-pointer capture plus `PointerCanceled` and `PointerCaptureLost`;
- `Tapped`, `DoubleTapped`, `RightTapped`, and `Holding` routed gestures;
- `ManipulationMode` and the Starting/Started/Delta/InertiaStarting/Completed event
  sequence, with translation, scale, rotation, expansion, velocity, and cumulative data;
- WinUI `ScrollViewer` touch panning, pinch zoom, view-changing/view-changed events,
  inertia, and `ChangeView`;
- `VisualStateManager`, `VisualStateGroup`, setters, state triggers, and
  `AdaptiveTrigger` breakpoints in effective pixels;
- adaptive `NavigationView` pane modes and the WinUI 640/1008 effective-pixel defaults;
- `InputScope`/enter-key/autocorrection metadata for editable controls and a neutral
  text-edit/composition channel for software keyboards and IMEs.

Existing mouse injection, `PointerRoutedEventArgs.Position`, control overrides, and
single-pointer `InputSystem.CapturePointer` calls remain source compatible.

## Architecture

1. **Platform records.** Hosts emit pointer ID, device kind, primary/contact state,
   buttons, pressure, contact rectangle, wheel data, modifiers, and a monotonic
   timestamp. Motion can be coalesced per pointer; down/up/cancel remain ordered.
2. **Window state.** Each `WindowInputState` owns active contacts, captures, hover,
   tap history, hold timers, and manipulation sessions. No interaction state is shared
   between windows.
3. **Routed input.** Hit testing occurs once at contact start. Pointer capture is keyed
   by ID. Event coordinates are transformed for each route target without changing the
   immutable screen-space point.
4. **Recognition.** Tap/hold thresholds and manipulation deltas are evaluated in
   logical pixels. Multi-contact scale/rotation use centroid and contact-vector changes.
   Gesture recognition remains CPU-side `O(P)` per sample for `P` active contacts (in
   normal UI use, `P` is a small bounded device count) and uses `O(P)` window storage.
5. **Controls.** Controls opt into manipulation through `ManipulationMode`. ScrollViewer
   owns pan/zoom policy and clamps offsets transactionally; other controls continue to
   consume ordinary routed pointer events.
6. **Browser text.** A DOM textarea is focused only when ProGPU focus selects an editable
   client during a user activation. `input`, `beforeinput`, and composition events are
   translated into explicit insert/delete/line-break/composition operations. Input
   scope maps to `inputmode`, enter-key hints, capitalization, spellcheck, and password
   behavior.
7. **Responsive metrics.** The browser canvas follows the visual viewport while the
   on-screen keyboard is visible and exposes safe-area/keyboard occlusion metrics.
   Adaptive triggers are reevaluated before layout when effective window metrics change.

## Delivery stages

- [x] Add pointer, gesture, manipulation, text-edit, viewport, and responsive public APIs.
- [x] Replace the single mouse/capture state with compatible per-pointer routing.
- [x] Add browser Pointer Events metadata, cancellation, coalescing, IME/software-keyboard
      input, VisualViewport/VirtualKeyboard geometry, and safe-area CSS behavior.
- [x] Adapt Avalonia and native desktop seams to preserve pointer device identity where
      their host APIs expose it.
- [x] Add touch pan/pinch/inertia to ScrollViewer and adaptive behavior to NavigationView.
- [x] Add VisualStateManager and AdaptiveTrigger evaluation and use them in Samples.
- [x] Make the Samples shell and representative fixed two-column pages usable at narrow
      portrait widths with minimal view churn; add an interactive input/gesture page.
- [x] Add focused API, routing, multi-touch, capture/cancel, recognition, mobile-edit,
      responsive-state, NavigationView, and ScrollViewer tests.
- [x] Build desktop and browser heads, run Release unit/headless suites, and record the
      exact totals and any environment-specific browser verification limitations.

## Primary sources and adopted decisions

- [WinUI pointer input](https://learn.microsoft.com/en-us/windows/apps/develop/input/handle-pointer-input):
  adopt device-neutral pointers, one ID per contact, per-pointer capture, cancellation,
  and extended point properties.
- [WinUI touch interactions](https://learn.microsoft.com/en-us/windows/apps/develop/input/touch-interactions):
  adopt direct manipulation, immediate feedback, standard tap/hold/pan/pinch conventions,
  touch targets, and portrait reflow guidance.
- [WinUI `UIElement.Tapped`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.tapped):
  preserve routed gesture ordering and the tap/hold exclusivity model.
- [WinUI `GestureRecognizer`](https://learn.microsoft.com/en-us/uwp/api/windows.ui.input.gesturerecognizer):
  adapt the settings and manipulation event model to ProGPU's retained visual tree.
- [WinUI responsive layouts](https://learn.microsoft.com/en-us/windows/apps/develop/ui/layouts-with-xaml):
  adopt VisualStateManager plus effective-pixel adaptive triggers.
- [WinUI NavigationView](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/navigationview):
  adopt `Auto`, `Left`, `LeftCompact`, `LeftMinimal`, and the 640/1008 defaults.
- [W3C Pointer Events 3](https://www.w3.org/TR/pointerevents3/): adopt Pointer Events,
  pointer capture/cancel semantics, primary/contact metadata, coalesced motion, and
  explicit `touch-action` ownership at the canvas.
- [W3C UI Events](https://www.w3.org/TR/uievents/) and
  [Input Events Level 2](https://www.w3.org/TR/input-events-2/): keep physical key events
  distinct from text edits and represent IME composition as an explicit session.
- [W3C VirtualKeyboard API](https://w3c.github.io/editing/docs/virtualkeyboard/): use
  keyboard geometry when available while retaining VisualViewport fallback behavior.

## Validation contract

- Event routing and capture are deterministic for simultaneous pointers and cancellation.
- Mouse behavior and existing controls remain source-compatible.
- Touch scrolling does not activate a child button after the pan threshold is crossed.
- A manipulation invalidates only the affected control/visual; no root-per-frame
  invalidation is introduced.
- Browser text entry covers insertion, surrogate pairs, backward/forward deletion,
  paste, line break, and composition without relying on mobile `KeyDown`/`KeyUp`.
- Layout breakpoints use logical/effective pixels, not framebuffer pixels.
- Browser rendering continues to size its swapchain in physical pixels at the active DPI.

## Completed verification

- Release desktop, Avalonia host, and browser builds completed with zero compile errors.
- The default trimmed WebAssembly AOT publish completed for all 68 compiled assemblies;
  the existing dependency-property and third-party trim-analysis warnings remain.
- `ProGPU.Tests` Release: 2,005 passed, 0 failed.
- `ProGPU.Tests.Headless` Release: 185 passed, 0 failed.
- A Chromium smoke run at 390 x 844 effective pixels verified the stacked Samples layout,
  scrollable navigation/content, focused DOM-to-ProGPU text entry with Unicode, and a
  touch Pointer Events sequence carrying identity, pressure, and contact dimensions with
  no browser console errors.
