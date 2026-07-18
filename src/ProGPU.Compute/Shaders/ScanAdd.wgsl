// Algorithm: Add the recursively scanned block prefix to every value of a 512-element scan block.
// Time complexity: O(N) total work with O(1) work per value.
// Space complexity: O(1) private storage, O(N) read-modify-write bandwidth, and O(ceil(N/512)) prefix reads.
// Workgroup: 256 threads; each thread updates two values. Unsigned addition intentionally wraps modulo 2^32.
struct ScanParams {
    count: u32,
    _pad0: u32,
    _pad1: u32,
    _pad2: u32,
};

@group(0) @binding(0) var<uniform> params: ScanParams;
@group(0) @binding(1) var<storage, read_write> outputValues: array<u32>;
@group(0) @binding(2) var<storage, read> blockOffsets: array<u32>;

@compute @workgroup_size(256)
fn scan_add(
    @builtin(local_invocation_id) localId: vec3<u32>,
    @builtin(workgroup_id) workgroupId: vec3<u32>) {
    let blockBase = workgroupId.x * 512u;
    let firstIndex = blockBase + localId.x;
    let secondIndex = firstIndex + 256u;
    let blockOffset = blockOffsets[workgroupId.x];
    if (firstIndex < params.count) {
        outputValues[firstIndex] = outputValues[firstIndex] + blockOffset;
    }
    if (secondIndex < params.count) {
        outputValues[secondIndex] = outputValues[secondIndex] + blockOffset;
    }
}
