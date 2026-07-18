// Algorithm: Transform four local draw-bound corners, intersect the conservative device AABB with viewport and optional clip bounds, and emit one visibility bit per draw.
// Time complexity: O(N) with fixed O(1) work, one root/instance matrix composition, and four matrix-vector products per draw.
// Space complexity: O(N) visibility output, O(N) metadata reads, O(T) transform storage, and O(1) private state per invocation.
// Workgroup: 256 threads. Bounds are conservative floating-point AABBs; NaN or invalid transform indices are classified invisible.
struct CullParams {
    rootTransform: mat4x4<f32>,
    viewport: vec4<f32>,
    drawCount: u32,
    transformCount: u32,
    _pad0: u32,
    _pad1: u32,
};

struct DrawMetadata {
    bounds: vec4<f32>,
    clipBounds: vec4<f32>,
    transformIndex: u32,
    sourceIndex: u32,
    materialKey: u32,
    flags: u32,
};

@group(0) @binding(0) var<uniform> params: CullParams;
@group(0) @binding(1) var<storage, read> draws: array<DrawMetadata>;
@group(0) @binding(2) var<storage, read> transforms: array<mat4x4<f32>>;
@group(0) @binding(3) var<storage, read_write> visibility: array<u32>;

@compute @workgroup_size(256)
fn scene_cull(@builtin(global_invocation_id) globalId: vec3<u32>) {
    let index = globalId.x;
    if (index >= params.drawCount) {
        return;
    }

    let draw = draws[index];
    if (draw.transformIndex >= params.transformCount) {
        visibility[index] = 0u;
        return;
    }

    let transform = params.rootTransform * transforms[draw.transformIndex];
    let p0 = (transform * vec4<f32>(draw.bounds.x, draw.bounds.y, 0.0, 1.0)).xy;
    let p1 = (transform * vec4<f32>(draw.bounds.z, draw.bounds.y, 0.0, 1.0)).xy;
    let p2 = (transform * vec4<f32>(draw.bounds.z, draw.bounds.w, 0.0, 1.0)).xy;
    let p3 = (transform * vec4<f32>(draw.bounds.x, draw.bounds.w, 0.0, 1.0)).xy;
    var minimum = min(min(p0, p1), min(p2, p3));
    var maximum = max(max(p0, p1), max(p2, p3));

    minimum = max(minimum, params.viewport.xy);
    maximum = min(maximum, params.viewport.zw);
    if ((draw.flags & 1u) != 0u) {
        minimum = max(minimum, draw.clipBounds.xy);
        maximum = min(maximum, draw.clipBounds.zw);
    }

    let finiteBounds = all(minimum == minimum) && all(maximum == maximum);
    visibility[index] = select(0u, 1u, finiteBounds && all(maximum > minimum));
}
