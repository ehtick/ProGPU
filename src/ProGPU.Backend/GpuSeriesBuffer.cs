using System;
using Silk.NET.WebGPU;

namespace ProGPU.Backend
{
    public unsafe class GpuSeriesBuffer : IDisposable
    {
        public GpuBuffer? Buffer { get; private set; }
        public int PointsCount { get; private set; }
        
        // WebGPU Uniform and BindGroup Cache
        public GpuBuffer? VsUniformBuffer { get; set; }
        public GpuBuffer? FsUniformBuffer { get; set; }
        public nint LineBindGroup { get; set; }
        public nint ScatterBindGroup { get; set; }
        public nint LineBindGroupOffscreen { get; set; }
        public nint ScatterBindGroupOffscreen { get; set; }
        public float[]? CachedInterleaved { get; set; }
        public object? AssociatedData { get; set; }
        public int AssociatedDataVersion { get; set; }
        
        private WgpuContext? _context;
        private bool _isDisposed;

        public bool IsOwnedBy(WgpuContext context)
        {
            return ReferenceEquals(_context, context);
        }

        public void Upload(float[] interleavedCoords, int pointsCount)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(GpuSeriesBuffer));

            PointsCount = pointsCount;
            uint requiredBytes = (uint)(interleavedCoords.Length * sizeof(float));

            var context = WgpuContext.Current;
            if (context == null)
            {
                throw new InvalidOperationException("WgpuContext.Current is not initialized. Cannot create GpuBuffer.");
            }

            if (_context != null && !ReferenceEquals(_context, context))
            {
                ReleaseBindGroups(_context);
                VsUniformBuffer?.Dispose();
                VsUniformBuffer = null;
                FsUniformBuffer?.Dispose();
                FsUniformBuffer = null;
                Buffer?.Dispose();
                Buffer = null;
            }

            _context = context;

            if (Buffer == null || Buffer.Size < requiredBytes)
            {
                ReleaseBindGroups();
                Buffer?.Dispose();
                
                // Create buffer with Vertex and Storage usage so both Line and Scatter shaders can use it
                Buffer = new GpuBuffer(
                    context,
                    Math.Max(64u, requiredBytes),
                    BufferUsage.Storage | BufferUsage.Vertex | BufferUsage.CopyDst,
                    "GpuSeriesBuffer"
                );
            }

            if (pointsCount > 0 && Buffer != null)
            {
                fixed (float* ptr = interleavedCoords)
                {
                    Buffer.Write(new ReadOnlySpan<float>(ptr, interleavedCoords.Length));
                }
            }
        }

        public void ReleaseBindGroups()
        {
            var context = _context ?? WgpuContext.Current;
            if (context == null) return;

            ReleaseBindGroups(context);
        }

        private void ReleaseBindGroups(WgpuContext context)
        {
            if (context.IsDisposed)
            {
                LineBindGroup = 0;
                ScatterBindGroup = 0;
                LineBindGroupOffscreen = 0;
                ScatterBindGroupOffscreen = 0;
                return;
            }

            if (LineBindGroup != 0)
            {
                context.Wgpu.BindGroupRelease((BindGroup*)LineBindGroup);
                LineBindGroup = 0;
            }
            if (ScatterBindGroup != 0)
            {
                context.Wgpu.BindGroupRelease((BindGroup*)ScatterBindGroup);
                ScatterBindGroup = 0;
            }
            if (LineBindGroupOffscreen != 0)
            {
                context.Wgpu.BindGroupRelease((BindGroup*)LineBindGroupOffscreen);
                LineBindGroupOffscreen = 0;
            }
            if (ScatterBindGroupOffscreen != 0)
            {
                context.Wgpu.BindGroupRelease((BindGroup*)ScatterBindGroupOffscreen);
                ScatterBindGroupOffscreen = 0;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            ReleaseBindGroups();
            
            VsUniformBuffer?.Dispose();
            VsUniformBuffer = null;
            
            FsUniformBuffer?.Dispose();
            FsUniformBuffer = null;

            Buffer?.Dispose();
            Buffer = null;
            
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }

        ~GpuSeriesBuffer()
        {
            // Safely clean up in finalizer if not explicitly disposed
        }
    }
}
