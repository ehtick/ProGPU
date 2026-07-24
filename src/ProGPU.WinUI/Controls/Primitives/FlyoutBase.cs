using System;
using System.Numerics;

namespace Microsoft.UI.Xaml.Controls.Primitives;

public enum FlyoutPlacementMode
{
    Top = 0,
    Bottom = 1,
    Left = 2,
    Right = 3,
    Full = 4,
    TopEdgeAlignedLeft = 5,
    TopEdgeAlignedRight = 6,
    BottomEdgeAlignedLeft = 7,
    BottomEdgeAlignedRight = 8,
    LeftEdgeAlignedTop = 9,
    LeftEdgeAlignedBottom = 10,
    RightEdgeAlignedTop = 11,
    RightEdgeAlignedBottom = 12,
    Auto = 13
}

public enum FlyoutShowMode
{
    Auto = 0,
    Standard = 1,
    Transient = 2,
    TransientWithDismissOnPointerMoveAway = 3
}

public sealed class FlyoutBaseClosingEventArgs : EventArgs
{
    public bool Cancel { get; set; }
}

public class FlyoutShowOptions
{
    public Windows.Foundation.Point? Position { get; set; }
    public Windows.Foundation.Rect? ExclusionRect { get; set; }
    public FlyoutShowMode ShowMode { get; set; }
    public FlyoutPlacementMode Placement { get; set; } = FlyoutPlacementMode.Auto;
}

public abstract class FlyoutBase : DependencyObject
{
    private Control? _presenter;

    public static readonly DependencyProperty PlacementProperty = Register(nameof(Placement), FlyoutPlacementMode.Top);
    public static readonly DependencyProperty AllowFocusOnInteractionProperty = Register(nameof(AllowFocusOnInteraction), true);
    public static readonly DependencyProperty LightDismissOverlayModeProperty = Register(nameof(LightDismissOverlayMode), LightDismissOverlayMode.Auto);
    public static readonly DependencyProperty AllowFocusWhenDisabledProperty = Register(nameof(AllowFocusWhenDisabled), false);
    public static readonly DependencyProperty ShowModeProperty = Register(nameof(ShowMode), FlyoutShowMode.Auto);
    public static readonly DependencyProperty AreOpenCloseAnimationsEnabledProperty = Register(nameof(AreOpenCloseAnimationsEnabled), true);
    public static readonly DependencyProperty ShouldConstrainToRootBoundsProperty = Register(nameof(ShouldConstrainToRootBounds), true);
    public static readonly DependencyProperty AttachedFlyoutProperty = DependencyProperty.RegisterAttached(
        "AttachedFlyout", typeof(FlyoutBase), typeof(FlyoutBase), new PropertyMetadata(null));

    public FlyoutPlacementMode Placement { get => Get<FlyoutPlacementMode>(PlacementProperty); set => SetValue(PlacementProperty, value); }
    public FrameworkElement? Target { get; private set; }
    public bool AllowFocusOnInteraction { get => Get<bool>(AllowFocusOnInteractionProperty); set => SetValue(AllowFocusOnInteractionProperty, value); }
    public LightDismissOverlayMode LightDismissOverlayMode { get => Get<LightDismissOverlayMode>(LightDismissOverlayModeProperty); set => SetValue(LightDismissOverlayModeProperty, value); }
    public bool AllowFocusWhenDisabled { get => Get<bool>(AllowFocusWhenDisabledProperty); set => SetValue(AllowFocusWhenDisabledProperty, value); }
    public FlyoutShowMode ShowMode { get => Get<FlyoutShowMode>(ShowModeProperty); set => SetValue(ShowModeProperty, value); }
    public bool AreOpenCloseAnimationsEnabled { get => Get<bool>(AreOpenCloseAnimationsEnabledProperty); set => SetValue(AreOpenCloseAnimationsEnabledProperty, value); }
    public bool ShouldConstrainToRootBounds { get => Get<bool>(ShouldConstrainToRootBoundsProperty); set => SetValue(ShouldConstrainToRootBoundsProperty, value); }
    public bool IsConstrainedToRootBounds => ShouldConstrainToRootBounds;
    public bool IsOpen => _presenter != null && PopupService.ActivePopups.Contains(_presenter);

    public event EventHandler<object?>? Opened;
    public event EventHandler<object?>? Closed;
    public event EventHandler<object?>? Opening;
    public event EventHandler<FlyoutBaseClosingEventArgs>? Closing;

    public void ShowAt(FrameworkElement placementTarget) => ShowAt(placementTarget, new FlyoutShowOptions());

    public void ShowAt(DependencyObject placementTarget, FlyoutShowOptions showOptions)
    {
        if (placementTarget is not FrameworkElement target)
            throw new ArgumentException("A flyout placement target must be a FrameworkElement.", nameof(placementTarget));
        ArgumentNullException.ThrowIfNull(showOptions);
        Hide();
        Target = target;
        Opening?.Invoke(this, null);
        _presenter = CreatePresenter();
        var origin = Vector2.Transform(Vector2.Zero, target.GetGlobalTransformMatrix());
        var position = showOptions.Position is { } requested
            ? origin + new Vector2((float)requested.X, (float)requested.Y)
            : GetPlacementPosition(origin, target.Size, showOptions.Placement == FlyoutPlacementMode.Auto ? Placement : showOptions.Placement);
        PopupService.ShowPopup(_presenter, position, target);
        Opened?.Invoke(this, null);
    }

    public void Hide()
    {
        if (_presenter == null) return;
        var args = new FlyoutBaseClosingEventArgs();
        Closing?.Invoke(this, args);
        if (args.Cancel) return;
        PopupService.HidePopup(_presenter);
        _presenter = null;
        Target = null;
        Closed?.Invoke(this, null);
    }

    public static FlyoutBase? GetAttachedFlyout(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(AttachedFlyoutProperty) as FlyoutBase;
    }

    public static void SetAttachedFlyout(FrameworkElement element, FlyoutBase? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AttachedFlyoutProperty, value);
    }

    public static void ShowAttachedFlyout(FrameworkElement flyoutOwner)
    {
        ArgumentNullException.ThrowIfNull(flyoutOwner);
        GetAttachedFlyout(flyoutOwner)?.ShowAt(flyoutOwner);
    }

    protected virtual Control CreatePresenter() => new ContentControl();

    private static Vector2 GetPlacementPosition(Vector2 origin, Vector2 size, FlyoutPlacementMode placement) => placement switch
    {
        FlyoutPlacementMode.Top or FlyoutPlacementMode.TopEdgeAlignedLeft or FlyoutPlacementMode.TopEdgeAlignedRight => origin,
        FlyoutPlacementMode.Left or FlyoutPlacementMode.LeftEdgeAlignedTop or FlyoutPlacementMode.LeftEdgeAlignedBottom => origin,
        FlyoutPlacementMode.Right or FlyoutPlacementMode.RightEdgeAlignedTop or FlyoutPlacementMode.RightEdgeAlignedBottom => origin + new Vector2(size.X, 0f),
        _ => origin + new Vector2(0f, size.Y)
    };

    private T Get<T>(DependencyProperty property) => (T)GetValue(property)!;
    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(FlyoutBase), new PropertyMetadata(defaultValue));
}
