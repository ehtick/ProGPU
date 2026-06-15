using System.Numerics;
using ProGPU.Scene;
using Xunit;

namespace ProGPU.Tests;

public sealed class VisualChangeVersionTests
{
    [Fact]
    public void PropertyChangeIncrementsChangeVersionEvenWhenAlreadyDirty()
    {
        var visual = new Visual();
        var initialVersion = visual.ChangeVersion;

        visual.Offset = new Vector2(10f, 20f);

        Assert.True(visual.IsDirty);
        Assert.True(visual.ChangeVersion > initialVersion);
    }

    [Fact]
    public void ClearingDirtyDoesNotIncrementChangeVersion()
    {
        var visual = new Visual
        {
            Offset = new Vector2(1f, 2f)
        };
        var changedVersion = visual.ChangeVersion;

        visual.IsDirty = false;

        Assert.False(visual.IsDirty);
        Assert.Equal(changedVersion, visual.ChangeVersion);
    }

    [Fact]
    public void SettingDirtyDirectlyIncrementsChangeVersion()
    {
        var visual = new Visual();
        visual.IsDirty = false;
        var cleanVersion = visual.ChangeVersion;

        visual.IsDirty = true;

        Assert.True(visual.IsDirty);
        Assert.True(visual.ChangeVersion > cleanVersion);
    }

    [Fact]
    public void ChildInvalidationIncrementsParentChangeVersion()
    {
        var parent = new ContainerVisual();
        var child = new Visual();
        parent.AddChild(child);
        parent.IsDirty = false;
        child.IsDirty = false;
        var parentVersion = parent.ChangeVersion;

        child.Opacity = 0.5f;

        Assert.True(child.IsDirty);
        Assert.True(parent.IsDirty);
        Assert.True(parent.ChangeVersion > parentVersion);
    }

    [Fact]
    public void ChildCollectionChangesIncrementParentChangeVersion()
    {
        var parent = new ContainerVisual();
        var initialVersion = parent.ChangeVersion;
        var child = new Visual();

        parent.AddChild(child);
        var addVersion = parent.ChangeVersion;
        parent.RemoveChild(child);

        Assert.True(addVersion > initialVersion);
        Assert.True(parent.ChangeVersion > addVersion);
    }
}
