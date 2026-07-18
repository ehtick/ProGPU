namespace ProGPU.Scene;

public sealed record CompositorOptions
{
    public static CompositorOptions Default { get; } = new();

    public uint GlyphAtlasSize { get; init; } = 2048;

    public uint GlyphAtlasPageCount { get; init; } = 4;

    public uint PathAtlasSize { get; init; } = 2048;

    public uint InitialVertexCount { get; init; } = 16384;

    public uint InitialIndexCount { get; init; } = 24576;

    public bool EnableGpuHitTesting { get; init; } = true;

    public bool EnableCompiledSceneCache { get; init; } = true;

    /// <summary>
    /// Enables experimental automatic promotion of text-only visuals into persistent fragment
    /// arenas. Disabled by default because recycled/scrolling text can increase draw fragmentation
    /// and GPU work even when CPU compilation decreases. Explicit scene fragments are unaffected.
    /// </summary>
    public bool EnableAutomaticTextFragments { get; init; }

    /// <summary>
    /// Enables stable GPU cull/scan/scatter and indirect replay for sufficiently large,
    /// homogeneous retained instance streams. Small streams retain direct cached replay.
    /// </summary>
    public bool EnableGpuSceneVisibility { get; init; } = true;

    public uint GpuSceneVisibilityMinimumItems { get; init; } = 2048;

    public uint PrimarySampleCount { get; init; } = 4;

    internal void Validate()
    {
        if (GlyphAtlasSize == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(GlyphAtlasSize));
        }
        if (GlyphAtlasPageCount == 0 || GlyphAtlasPageCount > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(GlyphAtlasPageCount));
        }
        if (PathAtlasSize == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PathAtlasSize));
        }
        if (InitialVertexCount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(InitialVertexCount));
        }
        if (InitialIndexCount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(InitialIndexCount));
        }
        if (PrimarySampleCount is not (1 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(PrimarySampleCount));
        }
        if (GpuSceneVisibilityMinimumItems == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(GpuSceneVisibilityMinimumItems));
        }
    }
}
