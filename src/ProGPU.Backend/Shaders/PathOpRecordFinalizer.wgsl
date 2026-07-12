// Algorithm: Finalize emitted path-operation records, clamp counters, and publish compact output metadata.
// Time complexity: O(R) for R generated records in the single finalizer invocation.
// Space complexity: O(1) local storage with O(R) storage-buffer traffic.
struct PathOpUniforms {
    op: u32,
    maxDestSegments: u32,
    _pad1: u32,
    _pad2: u32,
};

struct PathRecord {
    startSegment: u32,
    segmentCount: u32,
    minX: f32,
    minY: f32,
    maxX: f32,
    maxY: f32,
    fillRule: u32,
    _pad1: u32,
};

struct OutputSegments {
    count: u32,
    _pad0: u32,
    _pad1: u32,
    _pad2: u32,
};

@group(0) @binding(0) var<uniform> uniforms: PathOpUniforms;
@group(0) @binding(1) var<storage, read> recordA: PathRecord;
@group(0) @binding(2) var<storage, read> recordB: PathRecord;
@group(0) @binding(3) var<storage, read_write> destRecord: PathRecord;
@group(0) @binding(4) var<storage, read> destSegments: OutputSegments;

@compute @workgroup_size(1)
fn cs_main() {
    destRecord.startSegment = 0u;
    destRecord.segmentCount = min(destSegments.count, uniforms.maxDestSegments);
    destRecord.fillRule = 1u;
    destRecord._pad1 = 0u;

    let op = uniforms.op;
    if (op == 0u) { // Difference (A - B)
        destRecord.minX = recordA.minX;
        destRecord.minY = recordA.minY;
        destRecord.maxX = recordA.maxX;
        destRecord.maxY = recordA.maxY;
    } else if (op == 4u) { // Reverse Difference (B - A)
        destRecord.minX = recordB.minX;
        destRecord.minY = recordB.minY;
        destRecord.maxX = recordB.maxX;
        destRecord.maxY = recordB.maxY;
    } else if (op == 1u) { // Intersect
        destRecord.minX = max(recordA.minX, recordB.minX);
        destRecord.minY = max(recordA.minY, recordB.minY);
        destRecord.maxX = min(recordA.maxX, recordB.maxX);
        destRecord.maxY = min(recordA.maxY, recordB.maxY);
    } else { // Union / XOR
        destRecord.minX = min(recordA.minX, recordB.minX);
        destRecord.minY = min(recordA.minY, recordB.minY);
        destRecord.maxX = max(recordA.maxX, recordB.maxX);
        destRecord.maxY = max(recordA.maxY, recordB.maxY);
    }
}
