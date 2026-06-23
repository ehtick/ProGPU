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

    [Fact]
    public void VectorScalarAddSubBroadcastsScalarOperands()
    {
        const string glsl = """
void mainImage(out vec4 color, in vec2 coord) {
    vec2 uv = coord.xy;
    uv = uv + 1.0;
    uv = 2.0 - uv;
    color = vec4(uv, 0.0, 1.0);
}
""";

        var wgsl = ShaderToyTranspiler.Translate(glsl);

        Assert.Contains("uv = (uv + vec2<f32>(1.0));", wgsl);
        Assert.Contains("uv = (vec2<f32>(2.0) - uv);", wgsl);
    }

    [Fact]
    public void EmbeddedIncrementDecrementThrowsInsteadOfEmittingInvalidWgsl()
    {
        const string glsl = """
void mainImage(out vec4 color, in vec2 coord) {
    int i = 0;
    int j = i++;
    color = vec4(float(j), 0.0, 0.0, 1.0);
}
""";

        var exception = Assert.Throws<NotSupportedException>(() => ShaderToyTranspiler.Translate(glsl));

        Assert.Contains("embedded increment/decrement", exception.Message);
    }

    [Fact]
    public void StandaloneIncrementDecrementStillEmitsMutatingStatements()
    {
        const string glsl = """
void mainImage(out vec4 color, in vec2 coord) {
    int i = 0;
    i++;
    --i;
    color = vec4(float(i), 0.0, 0.0, 1.0);
}
""";

        var wgsl = ShaderToyTranspiler.Translate(glsl);

        Assert.Contains("i = i + 1;", wgsl);
        Assert.Contains("i = i - 1;", wgsl);
    }
}
