using ProGPU.Backend;

namespace ProGPU.Compute;

internal static class OpenTypeShapingShaders
{
    internal static readonly string Source =
        ShaderResource.Load(typeof(OpenTypeShapingShaders), "OpenTypeShaping.wgsl");
}
