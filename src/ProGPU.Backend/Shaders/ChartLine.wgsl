// Algorithm: Expand each chart segment into an anti-aliased screen-space quad and shade coverage from signed edge distance.
// Time complexity: O(1) per vertex and fragment.
// Space complexity: O(1) local storage and fixed vertex-buffer bandwidth.
const AA_PADDING: f32 = 1.5;

struct VSUniforms {
  transform       : mat4x4<f32>,
  canvasSize      : vec2<f32>,
  devicePixelRatio: f32,
  lineWidthCssPx  : f32,
  scale           : vec2<f32>,
  translate       : vec2<f32>,
};

@group(0) @binding(0) var<uniform> vsUniforms : VSUniforms;

struct FSUniforms {
  color : vec4<f32>,
};

@group(0) @binding(1) var<uniform> fsUniforms : FSUniforms;

@group(0) @binding(2) var<storage, read> points : array<vec2<f32>>;

struct VSOut {
  @builtin(position) clipPosition : vec4<f32>,
  @location(0) acrossDevice       : f32,
  @location(1) @interpolate(flat) widthDevice : f32,
};

fn quadUv(vid : u32) -> vec2<f32> {
  switch (vid) {
    case 0u: { return vec2<f32>(0.0, 0.0); }
    case 1u: { return vec2<f32>(1.0, 0.0); }
    case 2u: { return vec2<f32>(0.0, 1.0); }
    case 3u: { return vec2<f32>(0.0, 1.0); }
    case 4u: { return vec2<f32>(1.0, 0.0); }
    default: { return vec2<f32>(1.0, 1.0); }
  }
}

@vertex
fn vs_main(
  @builtin(vertex_index) vid : u32,
  @builtin(instance_index) iid : u32,
) -> VSOut {
  let uv = quadUv(vid);
  let pA_data = points[iid];
  let pB_data = points[iid + 1u];

  if (pA_data.x != pA_data.x || pA_data.y != pA_data.y ||
      pB_data.x != pB_data.x || pB_data.y != pB_data.y) {
    var out: VSOut;
    out.clipPosition = vec4<f32>(0.0, 0.0, 0.0, 0.0);
    out.acrossDevice = 0.0;
    out.widthDevice = 0.0;
    return out;
  }

  let pA_scaled = pA_data * vsUniforms.scale + vsUniforms.translate;
  let pB_scaled = pB_data * vsUniforms.scale + vsUniforms.translate;

  let clipA = vsUniforms.transform * vec4<f32>(pA_scaled, 0.0, 1.0);
  let clipB = vsUniforms.transform * vec4<f32>(pB_scaled, 0.0, 1.0);

  let ndcA = clipA.xy / clipA.w;
  let ndcB = clipB.xy / clipB.w;
  let screenA = vec2<f32>(
    (ndcA.x * 0.5 + 0.5) * vsUniforms.canvasSize.x,
    (1.0 - (ndcA.y * 0.5 + 0.5)) * vsUniforms.canvasSize.y,
  );
  let screenB = vec2<f32>(
    (ndcB.x * 0.5 + 0.5) * vsUniforms.canvasSize.x,
    (1.0 - (ndcB.y * 0.5 + 0.5)) * vsUniforms.canvasSize.y,
  );

  let delta = screenB - screenA;
  let segLen = length(delta);

  if (segLen < 1e-6) {
    var out : VSOut;
    out.clipPosition = clipA;
    out.acrossDevice = 0.0;
    out.widthDevice = 0.0;
    return out;
  }

  let dir = delta / segLen;
  let perp = vec2<f32>(dir.y, -dir.x);

  let widthHalfCss = vsUniforms.lineWidthCssPx * 0.5;
  let widthHalfDevice = widthHalfCss * vsUniforms.devicePixelRatio;
  let totalHalfDevice = widthHalfDevice + AA_PADDING;

  let offsetDevice = perp * totalHalfDevice * (uv.y * 2.0 - 1.0);
  let lengthOffsetDevice = dir * totalHalfDevice * (uv.x * (segLen + totalHalfDevice * 2.0) - totalHalfDevice);

  let pDevice = mix(screenA, screenB, uv.x) + offsetDevice;

  var out : VSOut;
  let pNdc = vec2<f32>(
    (pDevice.x / vsUniforms.canvasSize.x - 0.5) * 2.0,
    (1.0 - pDevice.y / vsUniforms.canvasSize.y - 0.5) * 2.0,
  );
  out.clipPosition = vec4<f32>(pNdc, 0.0, 1.0);
  out.acrossDevice = (uv.y * 2.0 - 1.0) * totalHalfDevice;
  out.widthDevice = widthHalfDevice;
  return out;
}

@fragment
fn fs_main(in : VSOut) -> @location(0) vec4<f32> {
  let dist = abs(in.acrossDevice) - in.widthDevice;
  let alpha = 1.0 - smoothstep(0.0, AA_PADDING, dist);
  if (alpha <= 0.0) {
    discard;
  }
  return vec4<f32>(fsUniforms.color.rgb, fsUniforms.color.a * alpha);
}
