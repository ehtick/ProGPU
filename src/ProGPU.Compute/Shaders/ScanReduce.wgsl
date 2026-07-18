// Algorithm: Perform an in-place Blelloch exclusive scan over 512 unsigned values per 256-thread workgroup and emit one block sum for hierarchical recursion.
// Time complexity: O(N) total work and O(log B) synchronized steps per block for N values and fixed block size B=512.
// Space complexity: O(B) workgroup storage, O(N) output bandwidth, and O(ceil(N/B)) block-sum storage.
// Workgroup: 256 threads; each thread loads and stores two values. Unsigned addition intentionally wraps modulo 2^32 to match WebGPU integer arithmetic.
struct ScanParams {
    count: u32,
    _pad0: u32,
    _pad1: u32,
    _pad2: u32,
};

@group(0) @binding(0) var<uniform> params: ScanParams;
@group(0) @binding(1) var<storage, read> inputValues: array<u32>;
@group(0) @binding(2) var<storage, read_write> outputValues: array<u32>;
@group(0) @binding(3) var<storage, read_write> blockSums: array<u32>;

var<workgroup> scanValues: array<u32, 512>;

@compute @workgroup_size(256)
fn scan_reduce(
    @builtin(local_invocation_id) localId: vec3<u32>,
    @builtin(workgroup_id) workgroupId: vec3<u32>) {
    let thread = localId.x;
    let blockBase = workgroupId.x * 512u;
    let firstIndex = blockBase + thread;
    let secondIndex = firstIndex + 256u;
    scanValues[thread] = 0u;
    scanValues[thread + 256u] = 0u;
    if (firstIndex < params.count) {
        scanValues[thread] = inputValues[firstIndex];
    }
    if (secondIndex < params.count) {
        scanValues[thread + 256u] = inputValues[secondIndex];
    }
    workgroupBarrier();

    // Fixed nine-step upsweep for B=512.
    var offset = 1u;
    var active_threads = 256u;
    loop {
        if (active_threads == 0u) { break; }
        if (thread < active_threads) {
            let left = offset * (2u * thread + 1u) - 1u;
            let right = offset * (2u * thread + 2u) - 1u;
            scanValues[right] = scanValues[right] + scanValues[left];
        }
        offset = offset * 2u;
        active_threads = active_threads / 2u;
        workgroupBarrier();
    }

    if (thread == 0u) {
        blockSums[workgroupId.x] = scanValues[511u];
        scanValues[511u] = 0u;
    }
    workgroupBarrier();

    // Fixed nine-step downsweep converts the reduction tree to an exclusive scan.
    active_threads = 1u;
    loop {
        if (active_threads > 256u) { break; }
        offset = offset / 2u;
        if (thread < active_threads) {
            let left = offset * (2u * thread + 1u) - 1u;
            let right = offset * (2u * thread + 2u) - 1u;
            let leftValue = scanValues[left];
            scanValues[left] = scanValues[right];
            scanValues[right] = scanValues[right] + leftValue;
        }
        active_threads = active_threads * 2u;
        workgroupBarrier();
    }

    if (firstIndex < params.count) {
        outputValues[firstIndex] = scanValues[thread];
    }
    if (secondIndex < params.count) {
        outputValues[secondIndex] = scanValues[thread + 256u];
    }
}
