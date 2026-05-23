using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using ProGPU.Backend;

namespace ProGPU.Vector;

[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct PathUniforms
{
    public float XStart;
    public float YStart;
    public float Scale;
    public uint PathIndex;
    public uint AtlasX;
    public uint AtlasY;
    public uint Width;
    public uint Height;
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct GpuPathRecord
{
    public uint StartSegment;
    public uint SegmentCount;
    public float MinX;
    public float MinY;
    public float MaxX;
    public float MaxY;
    public uint Pad0;
    public uint Pad1;
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct GpuPathSegment
{
    public Vector2 P0;
    public Vector2 P1;
    public Vector2 P2;
    public Vector2 P3;
    public uint SegmentType;
    public uint Pad0;
    public uint Pad1;
    public uint Pad2;
}

public unsafe class PathAtlas : IDisposable
{
    private readonly WgpuContext _context;
    private readonly GpuTexture _atlasTexture;
    private readonly uint _atlasSize;

    private uint _currentX = 2;
    private uint _currentY = 2;
    private uint _currentRowHeight = 0;

    public struct PathInfo
    {
        public uint X;
        public uint Y;
        public uint Width;
        public uint Height;
        public Vector2 TexCoordMin;
        public Vector2 TexCoordMax;
        public float MinX;
        public float MinY;
    }

    private readonly Dictionary<PathGeometry, PathInfo> _paths = new();
    private readonly List<GpuBuffer> _tempBuffers = new();

    private readonly RenderPipelineCache _pipelineCache;
    private readonly ComputePipeline* _computePipeline;
    private bool _isDisposed;

    public GpuTexture AtlasTexture => _atlasTexture;

    public PathAtlas(WgpuContext context, uint atlasSize = 2048)
    {
        _context = context;
        _atlasSize = atlasSize;

        _atlasTexture = new GpuTexture(
            _context,
            _atlasSize,
            _atlasSize,
            TextureFormat.R8Unorm,
            TextureUsage.TextureBinding | TextureUsage.CopyDst | TextureUsage.StorageBinding,
            "Dynamic Path Atlas"
        );

        byte[] clearData = new byte[_atlasSize * _atlasSize];
        _atlasTexture.WritePixels(new ReadOnlySpan<byte>(clearData));

        _pipelineCache = new RenderPipelineCache(_context);
        var shaderModule = _pipelineCache.GetOrCreateShader("PathRasterizer", Shaders.PathRasterizerShader, "PathRasterizerShader");
        _computePipeline = _pipelineCache.GetOrCreateComputePipeline("PathRasterizer", shaderModule, "cs_main");
    }

    private static uint DivRoundUp(uint value, uint divisor) => (value + divisor - 1) / divisor;

    public (GpuPathRecord[] Records, GpuPathSegment[] Segments) CompilePath(PathGeometry path, out float localMinX, out float localMinY, out float localMaxX, out float localMaxY)
    {
        var segments = new List<GpuPathSegment>();
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        void UpdateBounds(Vector2 p)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        foreach (var figure in path.Figures)
        {
            if (figure.Segments.Count == 0) continue;

            Vector2 currentPoint = figure.StartPoint;
            UpdateBounds(currentPoint);

            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line)
                {
                    segments.Add(new GpuPathSegment
                    {
                        P0 = currentPoint,
                        P1 = line.Point,
                        SegmentType = 0
                    });
                    UpdateBounds(line.Point);
                    currentPoint = line.Point;
                }
                else if (segment is QuadraticBezierSegment quad)
                {
                    segments.Add(new GpuPathSegment
                    {
                        P0 = currentPoint,
                        P1 = quad.ControlPoint,
                        P2 = quad.Point,
                        SegmentType = 1
                    });
                    UpdateBounds(quad.ControlPoint);
                    UpdateBounds(quad.Point);
                    currentPoint = quad.Point;
                }
                else if (segment is CubicBezierSegment cubic)
                {
                    segments.Add(new GpuPathSegment
                    {
                        P0 = currentPoint,
                        P1 = cubic.ControlPoint1,
                        P2 = cubic.ControlPoint2,
                        P3 = cubic.Point,
                        SegmentType = 2
                    });
                    UpdateBounds(cubic.ControlPoint1);
                    UpdateBounds(cubic.ControlPoint2);
                    UpdateBounds(cubic.Point);
                    currentPoint = cubic.Point;
                }
            }

            if (figure.IsClosed && currentPoint != figure.StartPoint)
            {
                segments.Add(new GpuPathSegment
                {
                    P0 = currentPoint,
                    P1 = figure.StartPoint,
                    SegmentType = 0
                });
                UpdateBounds(figure.StartPoint);
            }
        }

        if (segments.Count == 0)
        {
            localMinX = localMinY = localMaxX = localMaxY = 0f;
            return (Array.Empty<GpuPathRecord>(), Array.Empty<GpuPathSegment>());
        }

        localMinX = minX;
        localMinY = minY;
        localMaxX = maxX;
        localMaxY = maxY;

        var records = new GpuPathRecord[1];
        records[0] = new GpuPathRecord
        {
            StartSegment = 0,
            SegmentCount = (uint)segments.Count,
            MinX = minX,
            MinY = minY,
            MaxX = maxX,
            MaxY = maxY
        };

        return (records, segments.ToArray());
    }

    public PathInfo GetOrCreatePath(PathGeometry path)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(PathAtlas));

        if (_paths.TryGetValue(path, out var info))
        {
            return info;
        }

        var (records, segments) = CompilePath(path, out float minX, out float minY, out float maxX, out float maxY);

        if (records.Length == 0 || segments.Length == 0)
        {
            info = new PathInfo { X = 0, Y = 0, Width = 0, Height = 0, TexCoordMin = Vector2.Zero, TexCoordMax = Vector2.Zero, MinX = 0f, MinY = 0f };
            _paths[path] = info;
            return info;
        }

        // Bounding box padding for anti-aliasing
        int padding = 4;
        int xStart = (int)Math.Floor(minX) - padding;
        int xEnd = (int)Math.Ceiling(maxX) + padding;
        int yStart = (int)Math.Floor(minY) - padding;
        int yEnd = (int)Math.Ceiling(maxY) + padding;

        int width = xEnd - xStart;
        int height = yEnd - yStart;

        if (width <= 0 || height <= 0)
        {
            info = new PathInfo { X = 0, Y = 0, Width = 0, Height = 0, TexCoordMin = Vector2.Zero, TexCoordMax = Vector2.Zero, MinX = 0f, MinY = 0f };
            _paths[path] = info;
            return info;
        }

        uint gW = (uint)width;
        uint gH = (uint)height;

        if (_currentX + gW + 2 > _atlasSize)
        {
            _currentX = 2;
            _currentY += _currentRowHeight + 2;
            _currentRowHeight = 0;
        }

        if (_currentY + gH + 2 > _atlasSize)
        {
            Console.WriteLine("[PathAtlas] Warning: Texture Atlas is full! Clearing cache.");
            _paths.Clear();
            _currentX = 2;
            _currentY = 2;
            _currentRowHeight = 0;

            byte[] clearData = new byte[_atlasSize * _atlasSize];
            _atlasTexture.WritePixels(new ReadOnlySpan<byte>(clearData));
        }

        uint posX = _currentX;
        uint posY = _currentY;

        _currentX += gW + 2;
        _currentRowHeight = Math.Max(_currentRowHeight, gH);

        // Upload records and segments
        var recordsBuffer = new GpuBuffer(
            _context,
            (uint)(records.Length * Marshal.SizeOf<GpuPathRecord>()),
            BufferUsage.Storage | BufferUsage.CopyDst,
            "Path Records Buffer"
        );
        recordsBuffer.Write(new ReadOnlySpan<GpuPathRecord>(records));
        _tempBuffers.Add(recordsBuffer);

        var segmentsBuffer = new GpuBuffer(
            _context,
            (uint)(segments.Length * Marshal.SizeOf<GpuPathSegment>()),
            BufferUsage.Storage | BufferUsage.CopyDst,
            "Path Segments Buffer"
        );
        segmentsBuffer.Write(new ReadOnlySpan<GpuPathSegment>(segments));
        _tempBuffers.Add(segmentsBuffer);

        var uniforms = new PathUniforms
        {
            XStart = xStart,
            YStart = yStart,
            Scale = 1.0f,
            PathIndex = 0,
            AtlasX = posX,
            AtlasY = posY,
            Width = gW,
            Height = gH
        };

        var uniformsBuffer = new GpuBuffer(
            _context,
            (uint)Marshal.SizeOf<PathUniforms>(),
            BufferUsage.Uniform | BufferUsage.CopyDst,
            "Path Uniforms Buffer"
        );
        uniformsBuffer.WriteSingle(uniforms);
        _tempBuffers.Add(uniformsBuffer);

        // Get bind group layout and dispatch compute shader
        var bindGroupLayout = _context.Wgpu.ComputePipelineGetBindGroupLayout(_computePipeline, 0);

        var entries = stackalloc BindGroupEntry[4];
        entries[0] = new BindGroupEntry { Binding = 0, Buffer = uniformsBuffer.BufferPtr, Offset = 0, Size = uniformsBuffer.Size };
        entries[1] = new BindGroupEntry { Binding = 1, Buffer = recordsBuffer.BufferPtr, Offset = 0, Size = recordsBuffer.Size };
        entries[2] = new BindGroupEntry { Binding = 2, Buffer = segmentsBuffer.BufferPtr, Offset = 0, Size = segmentsBuffer.Size };
        entries[3] = new BindGroupEntry { Binding = 3, TextureView = _atlasTexture.ViewPtr };

        var bgDesc = new BindGroupDescriptor
        {
            Layout = bindGroupLayout,
            EntryCount = 4,
            Entries = entries
        };
        var bg = _context.Wgpu.DeviceCreateBindGroup(_context.Device, &bgDesc);

        var encoderDesc = new CommandEncoderDescriptor { Label = (byte*)SilkMarshal.StringToPtr("Path Rasterizer Encoder") };
        var encoder = _context.Wgpu.DeviceCreateCommandEncoder(_context.Device, &encoderDesc);
        SilkMarshal.Free((nint)encoderDesc.Label);

        var passDesc = new ComputePassDescriptor();
        var pass = _context.Wgpu.CommandEncoderBeginComputePass(encoder, &passDesc);

        _context.Wgpu.ComputePassEncoderSetPipeline(pass, _computePipeline);
        _context.Wgpu.ComputePassEncoderSetBindGroup(pass, 0, bg, 0, null);

        uint workgroupsX = DivRoundUp(gW, 16);
        uint workgroupsY = DivRoundUp(gH, 16);
        _context.Wgpu.ComputePassEncoderDispatchWorkgroups(pass, workgroupsX, workgroupsY, 1);

        _context.Wgpu.ComputePassEncoderEnd(pass);
        _context.Wgpu.ComputePassEncoderRelease(pass);

        var cmdDesc = new CommandBufferDescriptor { Label = (byte*)SilkMarshal.StringToPtr("Path Rasterizer Command Buffer") };
        var cmdBuffer = _context.Wgpu.CommandEncoderFinish(encoder, &cmdDesc);
        SilkMarshal.Free((nint)cmdDesc.Label);

        _context.Wgpu.QueueSubmit(_context.Queue, 1, &cmdBuffer);

        _context.Wgpu.CommandBufferRelease(cmdBuffer);
        _context.Wgpu.CommandEncoderRelease(encoder);
        _context.Wgpu.BindGroupRelease(bg);
        _context.Wgpu.BindGroupLayoutRelease(bindGroupLayout);

        float texelSize = 1.0f / _atlasSize;
        info = new PathInfo
        {
            X = posX,
            Y = posY,
            Width = gW,
            Height = gH,
            TexCoordMin = new Vector2(posX * texelSize, posY * texelSize),
            TexCoordMax = new Vector2((posX + gW) * texelSize, (posY + gH) * texelSize),
            MinX = xStart,
            MinY = yStart
        };

        _paths[path] = info;
        return info;
    }

    public void CleanupFrame()
    {
        foreach (var buffer in _tempBuffers)
        {
            buffer.Dispose();
        }
        _tempBuffers.Clear();
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        CleanupFrame();
        _pipelineCache.Dispose();
        _atlasTexture.Dispose();
        _paths.Clear();

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    ~PathAtlas()
    {
        Dispose();
    }
}
