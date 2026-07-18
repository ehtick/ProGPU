using System.Numerics;
using ProGPU.Scene;
using ProGPU.Vector;
using Xunit;

namespace ProGPU.Tests;

public sealed class SceneFragmentHandleTests
{
    [Fact]
    public void DrawingContextRecordsStableFragmentIdentityAndTransforms()
    {
        using var handle = new SceneFragmentHandle(CreatePicture());
        var transform = new SceneTransformHandle { Translation = new Vector2(12f, 34f) };
        var baseTransform = Matrix4x4.CreateScale(2f);
        var context = new DrawingContext();

        context.DrawSceneFragment(handle, transform, baseTransform);

        var command = Assert.Single(context.Commands);
        Assert.Equal(RenderCommandType.DrawSceneFragment, command.Type);
        Assert.Same(handle, command.SceneFragment);
        Assert.Same(transform, command.SceneTransform);
        Assert.Equal(baseTransform, command.Transform);
    }

    [Fact]
    public void ReplacingPictureAdvancesVersionAndDisposesPreviousPicture()
    {
        var first = CreatePicture();
        var second = CreatePicture();
        using var handle = new SceneFragmentHandle(first);
        var version = handle.Version;

        handle.ReplacePicture(second);

        Assert.True(handle.Version > version);
        Assert.Throws<ObjectDisposedException>(() => first.Clone());
        using var clone = second.Clone();
    }

    [Fact]
    public void ReplacementAfterDisposalIsRejectedAndDisposed()
    {
        var handle = new SceneFragmentHandle(CreatePicture());
        handle.Dispose();
        var replacement = CreatePicture();

        Assert.Throws<ObjectDisposedException>(() => handle.ReplacePicture(replacement));
        Assert.Throws<ObjectDisposedException>(() => replacement.Clone());
    }

    private static GpuPicture CreatePicture() => new(
        [],
        [],
        [],
        [],
        []);
}
