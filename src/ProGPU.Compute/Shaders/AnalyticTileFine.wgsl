// Algorithm: Consume painter-ordered fill commands for each 16x16 tile, integrate exact signed trapezoid area from tile-clipped line segments plus the row backdrop, apply non-zero or even-odd winding, and source-over composite premultiplied solid colors.
// Time complexity: O(T * 256 * (C + S)) worst case for T dispatched tiles, C commands per tile, and S tile-local segments across those commands; each of 64 threads produces four pixels and performs fixed-work analytic integration per segment.
// Space complexity: O(T + C + S) read-only scene storage, O(P) output bandwidth for P covered target pixels, and O(1) private storage (four coverage and four color values) per invocation; no workgroup storage, atomics, barriers, BVH traversal, or distance-field evaluation.
// Workgroup: 4x16 threads cover one 16x16 tile. Segment endpoints and y_edge are tile-local
// coordinates produced by path tiling. Colors are premultiplied linear values. The 1e-6 x bias
// matches Vello's analytic-area denominator guard and prevents a zero-width division for verticals.
// Coverage integration is adapted from Vello's fine shader (Apache-2.0 OR MIT OR Unlicense):
// https://github.com/linebender/vello/blob/main/vello_shaders/shader/fine.wgsl
struct FineParams {
    width: u32,
    height: u32,
    tileCount: u32,
    _pad0: u32,
};

struct Tile {
    x: u32,
    y: u32,
    commandOffset: u32,
    commandCount: u32,
};

struct FillCommand {
    segmentOffset: u32,
    segmentCountAndRule: u32,
    backdrop: i32,
    _pad0: u32,
    premultipliedColor: vec4<f32>,
};

struct Segment {
    point0: vec2<f32>,
    point1: vec2<f32>,
    yEdge: f32,
    _pad0: f32,
};

@group(0) @binding(0) var<uniform> params: FineParams;
@group(0) @binding(1) var<storage, read> tiles: array<Tile>;
@group(0) @binding(2) var<storage, read> commands: array<FillCommand>;
@group(0) @binding(3) var<storage, read> segments: array<Segment>;
@group(0) @binding(4) var outputTexture: texture_storage_2d<rgba8unorm, write>;

fn source_over(source: vec4<f32>, destination: vec4<f32>) -> vec4<f32> {
    return source + destination * (1.0 - source.a);
}

fn segment_area(minimumX: f32, maximumX: f32, pixelX: f32) -> f32 {
    let xmin = min(minimumX - pixelX, 1.0) - 1.0e-6;
    let xmax = maximumX - pixelX;
    let b = min(xmax, 1.0);
    let c = max(b, 0.0);
    let d = max(xmin, 0.0);
    return (b + 0.5 * (d * d - c * c) - xmin) / (xmax - xmin);
}

@compute @workgroup_size(4, 16, 1)
fn analytic_tile_fine(
    @builtin(workgroup_id) workgroupId: vec3<u32>,
    @builtin(local_invocation_id) localId: vec3<u32>) {
    let tileIndex = workgroupId.x + workgroupId.y * 65535u;
    if (tileIndex >= params.tileCount) {
        return;
    }

    let tile = tiles[tileIndex];
    let basePixel = vec2<u32>(tile.x * 16u + localId.x * 4u, tile.y * 16u + localId.y);
    var rgba0 = vec4<f32>(0.0);
    var rgba1 = vec4<f32>(0.0);
    var rgba2 = vec4<f32>(0.0);
    var rgba3 = vec4<f32>(0.0);

    for (var commandIndex = 0u; commandIndex < tile.commandCount; commandIndex += 1u) {
        let command = commands[tile.commandOffset + commandIndex];
        let segmentCount = command.segmentCountAndRule >> 1u;
        let evenOdd = (command.segmentCountAndRule & 1u) != 0u;
        var area0 = f32(command.backdrop);
        var area1 = area0;
        var area2 = area0;
        var area3 = area0;
        let yPixel = f32(localId.y);

        for (var segmentIndex = 0u; segmentIndex < segmentCount; segmentIndex += 1u) {
            let segment = segments[command.segmentOffset + segmentIndex];
            let delta = segment.point1 - segment.point0;
            let y = segment.point0.y - yPixel;
            let y0 = clamp(y, 0.0, 1.0);
            let y1 = clamp(y + delta.y, 0.0, 1.0);
            let dy = y0 - y1;
            let yEdge = sign(delta.x) * clamp(yPixel - segment.yEdge + 1.0, 0.0, 1.0);

            if (dy != 0.0) {
                let inverseY = 1.0 / delta.y;
                let t0 = (y0 - y) * inverseY;
                let t1 = (y1 - y) * inverseY;
                let x0 = segment.point0.x + t0 * delta.x;
                let x1 = segment.point0.x + t1 * delta.x;
                let minimumX = min(x0, x1);
                let maximumX = max(x0, x1);

                let firstPixelX = f32(localId.x * 4u);
                area0 += yEdge + segment_area(minimumX, maximumX, firstPixelX) * dy;
                area1 += yEdge + segment_area(minimumX, maximumX, firstPixelX + 1.0) * dy;
                area2 += yEdge + segment_area(minimumX, maximumX, firstPixelX + 2.0) * dy;
                area3 += yEdge + segment_area(minimumX, maximumX, firstPixelX + 3.0) * dy;
            } else {
                area0 += yEdge;
                area1 += yEdge;
                area2 += yEdge;
                area3 += yEdge;
            }
        }

        if (evenOdd) {
            area0 = abs(area0 - 2.0 * round(0.5 * area0));
            area1 = abs(area1 - 2.0 * round(0.5 * area1));
            area2 = abs(area2 - 2.0 * round(0.5 * area2));
            area3 = abs(area3 - 2.0 * round(0.5 * area3));
        } else {
            area0 = min(abs(area0), 1.0);
            area1 = min(abs(area1), 1.0);
            area2 = min(abs(area2), 1.0);
            area3 = min(abs(area3), 1.0);
        }

        rgba0 = source_over(command.premultipliedColor * area0, rgba0);
        rgba1 = source_over(command.premultipliedColor * area1, rgba1);
        rgba2 = source_over(command.premultipliedColor * area2, rgba2);
        rgba3 = source_over(command.premultipliedColor * area3, rgba3);
    }

    if (basePixel.y < params.height) {
        if (basePixel.x < params.width) {
            textureStore(outputTexture, vec2<i32>(basePixel), rgba0);
        }
        if (basePixel.x + 1u < params.width) {
            textureStore(outputTexture, vec2<i32>(basePixel + vec2<u32>(1u, 0u)), rgba1);
        }
        if (basePixel.x + 2u < params.width) {
            textureStore(outputTexture, vec2<i32>(basePixel + vec2<u32>(2u, 0u)), rgba2);
        }
        if (basePixel.x + 3u < params.width) {
            textureStore(outputTexture, vec2<i32>(basePixel + vec2<u32>(3u, 0u)), rgba3);
        }
    }
}
