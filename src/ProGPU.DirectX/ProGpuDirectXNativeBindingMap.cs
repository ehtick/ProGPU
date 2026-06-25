namespace ProGPU.DirectX;

internal static class ProGpuDirectXNativeBindingMap
{
    private const uint StageStride = 512;
    private const uint ConstantBufferBase = 0;
    private const uint ShaderResourceBase = 64;
    private const uint SamplerBase = 256;
    private const uint UnorderedAccessBase = 320;

    public static uint GetNativeBinding(DxShaderStage stage, ProGpuDirectXBindingKind kind, uint slot)
    {
        return GetStageBase(stage) + GetKindBase(kind) + slot;
    }

    public static uint GetConstantBufferBinding(DxShaderStage stage, uint slot)
    {
        return GetStageBase(stage) + ConstantBufferBase + slot;
    }

    public static uint GetShaderResourceBinding(DxShaderStage stage, uint slot)
    {
        return GetStageBase(stage) + ShaderResourceBase + slot;
    }

    public static uint GetSamplerBinding(DxShaderStage stage, uint slot)
    {
        return GetStageBase(stage) + SamplerBase + slot;
    }

    private static uint GetStageBase(DxShaderStage stage)
    {
        return stage switch
        {
            DxShaderStage.Vertex => 0,
            DxShaderStage.Pixel => StageStride,
            DxShaderStage.Geometry => StageStride * 2,
            DxShaderStage.Compute => StageStride * 3,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
        };
    }

    private static uint GetKindBase(ProGpuDirectXBindingKind kind)
    {
        return kind switch
        {
            ProGpuDirectXBindingKind.ConstantBuffer => ConstantBufferBase,
            ProGpuDirectXBindingKind.ShaderResourceView => ShaderResourceBase,
            ProGpuDirectXBindingKind.Sampler => SamplerBase,
            ProGpuDirectXBindingKind.UnorderedAccessView => UnorderedAccessBase,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }
}
