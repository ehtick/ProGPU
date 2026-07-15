using ProGPU.Backend;

namespace ProGPU.Compute;

public static class WavefrontShaders
{
    public static readonly string ShadersSource =
        ShaderResource.Load(typeof(WavefrontShaders), "Wavefront.wgsl");
}
