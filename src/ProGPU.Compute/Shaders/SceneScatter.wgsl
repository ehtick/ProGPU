// Algorithm: Use exclusive visibility offsets to scatter visible source/material indices in stable painter order and write total count plus one homogeneous indirect draw command.
// Time complexity: O(N) with O(1) work per draw.
// Space complexity: O(V) compact outputs for V visible draws, two O(N) flag/offset reads, one count write, and one 16-byte indirect command write.
// Workgroup: 256 threads. Distinct visible inputs have distinct exclusive offsets, so scatter is race-free without atomics.
struct ScatterParams {
    drawCount: u32,
    vertexCount: u32,
    firstVertex: u32,
    firstInstance: u32,
};

struct DrawMetadata {
    bounds: vec4<f32>,
    clipBounds: vec4<f32>,
    transformIndex: u32,
    sourceIndex: u32,
    materialKey: u32,
    flags: u32,
};

struct DrawIndirectArgs {
    vertexCount: u32,
    instanceCount: u32,
    firstVertex: u32,
    firstInstance: u32,
};

@group(0) @binding(0) var<uniform> params: ScatterParams;
@group(0) @binding(1) var<storage, read> draws: array<DrawMetadata>;
@group(0) @binding(2) var<storage, read> visibility: array<u32>;
@group(0) @binding(3) var<storage, read> offsets: array<u32>;
@group(0) @binding(4) var<storage, read_write> visibleSourceIndices: array<u32>;
@group(0) @binding(5) var<storage, read_write> visibleMaterialKeys: array<u32>;
@group(0) @binding(6) var<storage, read_write> visibleCount: array<u32>;
@group(0) @binding(7) var<storage, read_write> indirectArgs: array<DrawIndirectArgs>;

@compute @workgroup_size(256)
fn scene_scatter(@builtin(global_invocation_id) globalId: vec3<u32>) {
    let index = globalId.x;
    if (index >= params.drawCount) {
        return;
    }

    let flag = visibility[index];
    let offset = offsets[index];
    if (flag != 0u) {
        visibleSourceIndices[offset] = draws[index].sourceIndex;
        visibleMaterialKeys[offset] = draws[index].materialKey;
    }

    if (index + 1u == params.drawCount) {
        let count = offset + flag;
        visibleCount[0] = count;
        indirectArgs[0] = DrawIndirectArgs(
            params.vertexCount,
            count,
            params.firstVertex,
            params.firstInstance);
    }
}
