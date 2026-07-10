namespace ProGPU.Scene;

public sealed record CompositorOptions
{
    public static CompositorOptions Default { get; } = new();

    public uint GlyphAtlasSize { get; init; } = 2048;

    public uint PathAtlasSize { get; init; } = 2048;

    public uint InitialVertexCount { get; init; } = 16384;

    public uint InitialIndexCount { get; init; } = 24576;

    internal void Validate()
    {
        if (GlyphAtlasSize == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(GlyphAtlasSize));
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
    }
}
