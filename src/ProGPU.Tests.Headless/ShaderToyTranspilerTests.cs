using ProGPU.Transpiler;
using Xunit;

namespace ProGPU.Tests.Headless;

public sealed class ShaderToyTranspilerTests
{
    [Fact]
    public void MainImageBareReturnReturnsCurrentFragColor()
    {
        const string glsl = """
void mainImage(out vec4 color, in vec2 coord) {
    color = vec4(1.0, 0.0, 0.0, 1.0);
    if (coord.x < 0.5) {
        return;
    }
    color = vec4(0.0, 1.0, 0.0, 1.0);
}
""";

        var wgsl = ShaderToyTranspiler.Translate(glsl);

        Assert.Contains("fn mainImage(coord: vec2<f32>) -> vec4<f32>", wgsl);
        Assert.Contains("return color;", wgsl);
        Assert.DoesNotContain("return;\n", wgsl);
    }
}
