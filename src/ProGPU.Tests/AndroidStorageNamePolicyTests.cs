using ProGPU.Android;
using Xunit;

namespace ProGPU.Tests;

public sealed class AndroidStorageNamePolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    public void UnsafeOrEmptySegmentsUseFallback(string? value)
    {
        Assert.Equal("document", AndroidStorageNamePolicy.SanitizeFileName(value, "document"));
    }

    [Fact]
    public void TraversalSeparatorIsReplaced()
    {
        string result = AndroidStorageNamePolicy.SanitizeFileName("../secret.txt", "document");

        Assert.DoesNotContain(Path.DirectorySeparatorChar, result);
        Assert.DoesNotContain(Path.AltDirectorySeparatorChar, result);
        Assert.NotEqual("..", result);
    }

    [Fact]
    public void OrdinaryDisplayNameIsPreserved()
    {
        Assert.Equal(
            "drawing.dxf",
            AndroidStorageNamePolicy.SanitizeFileName("drawing.dxf", "document"));
    }
}
