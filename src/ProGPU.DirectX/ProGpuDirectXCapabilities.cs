namespace ProGPU.DirectX;

public sealed class ProGpuDirectXCapabilities
{
    private static readonly DxFeatureLevel[] s_featureLevels =
    [
        DxFeatureLevel.Direct3D11_1,
        DxFeatureLevel.Direct3D11_0,
        DxFeatureLevel.Direct3D10_1,
        DxFeatureLevel.Direct3D10_0,
        DxFeatureLevel.Direct3D9_3
    ];

    internal ProGpuDirectXCapabilities(
        bool isGpuBacked,
        uint maxTextureDimension2D,
        bool supportsReadWriteStorageTextures)
    {
        IsGpuBacked = isGpuBacked;
        MaxTextureDimension2D = maxTextureDimension2D;
        SupportsReadWriteStorageTextures = supportsReadWriteStorageTextures;
    }

    public bool IsGpuBacked { get; }

    public IReadOnlyList<DxFeatureLevel> SupportedFeatureLevels => s_featureLevels;

    public DxFeatureLevel HighestFeatureLevel => s_featureLevels[0];

    public uint MaxTextureDimension2D { get; }

    public bool SupportsReadWriteStorageTextures { get; }

    public bool SupportsFeatureLevel(DxFeatureLevel featureLevel)
    {
        return s_featureLevels.Contains(featureLevel);
    }

    public bool SupportsFormat(DxResourceFormat format)
    {
        return format is not DxResourceFormat.Unknown;
    }
}
