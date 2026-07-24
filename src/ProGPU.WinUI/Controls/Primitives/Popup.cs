using System;
using System.Numerics;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media.Animation;
using ProGPU.Layout;
using ProGPU.Scene;

namespace Microsoft.UI.Xaml.Controls.Primitives;

public enum PopupPlacementMode
{
    Auto = 0,
    Top = 1,
    Bottom = 2,
    Left = 3,
    Right = 4,
    TopEdgeAlignedLeft = 5,
    TopEdgeAlignedRight = 6,
    BottomEdgeAlignedLeft = 7,
    BottomEdgeAlignedRight = 8,
    LeftEdgeAlignedTop = 9,
    LeftEdgeAlignedBottom = 10,
    RightEdgeAlignedTop = 11,
    RightEdgeAlignedBottom = 12
}

[ContentProperty(Name = nameof(Child))]
public sealed class Popup : FrameworkElement
{
    public static readonly DependencyProperty ChildProperty = Register<UIElement?>(nameof(Child), null, OnChildChanged);
    public static readonly DependencyProperty IsOpenProperty = Register(nameof(IsOpen), false, OnIsOpenChanged);
    public static readonly DependencyProperty HorizontalOffsetProperty = Register(nameof(HorizontalOffset), 0d, OnPlacementChanged);
    public static readonly DependencyProperty VerticalOffsetProperty = Register(nameof(VerticalOffset), 0d, OnPlacementChanged);
    public static readonly DependencyProperty ChildTransitionsProperty = Register<TransitionCollection?>(nameof(ChildTransitions), null);
    public static readonly DependencyProperty IsLightDismissEnabledProperty = Register(nameof(IsLightDismissEnabled), false);
    public static readonly DependencyProperty LightDismissOverlayModeProperty = Register(nameof(LightDismissOverlayMode), LightDismissOverlayMode.Auto);
    public static readonly DependencyProperty ShouldConstrainToRootBoundsProperty = Register(nameof(ShouldConstrainToRootBounds), true);
    public static readonly DependencyProperty PlacementTargetProperty = Register<FrameworkElement?>(nameof(PlacementTarget), null);
    public static readonly DependencyProperty DesiredPlacementProperty = Register(nameof(DesiredPlacement), PopupPlacementMode.Auto);

    public UIElement? Child { get => GetValue(ChildProperty) as UIElement; set => SetValue(ChildProperty, value); }
    public bool IsOpen { get => Get<bool>(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public double HorizontalOffset { get => Get<double>(HorizontalOffsetProperty); set => SetValue(HorizontalOffsetProperty, value); }
    public double VerticalOffset { get => Get<double>(VerticalOffsetProperty); set => SetValue(VerticalOffsetProperty, value); }
    public TransitionCollection? ChildTransitions { get => GetValue(ChildTransitionsProperty) as TransitionCollection; set => SetValue(ChildTransitionsProperty, value); }
    public bool IsLightDismissEnabled { get => Get<bool>(IsLightDismissEnabledProperty); set => SetValue(IsLightDismissEnabledProperty, value); }
    public LightDismissOverlayMode LightDismissOverlayMode { get => Get<LightDismissOverlayMode>(LightDismissOverlayModeProperty); set => SetValue(LightDismissOverlayModeProperty, value); }
    public bool ShouldConstrainToRootBounds { get => Get<bool>(ShouldConstrainToRootBoundsProperty); set => SetValue(ShouldConstrainToRootBoundsProperty, value); }
    public bool IsConstrainedToRootBounds => ShouldConstrainToRootBounds;
    public FrameworkElement? PlacementTarget { get => GetValue(PlacementTargetProperty) as FrameworkElement; set => SetValue(PlacementTargetProperty, value); }
    public PopupPlacementMode DesiredPlacement { get => Get<PopupPlacementMode>(DesiredPlacementProperty); set => SetValue(DesiredPlacementProperty, value); }
    public PopupPlacementMode ActualPlacement => DesiredPlacement;

    public event EventHandler<object?>? Opened;
    public event EventHandler<object?>? Closed;
    public event EventHandler<object?>? ActualPlacementChanged;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (Child is not FrameworkElement child) return Vector2.Zero;
        child.Measure(availableSize);
        return child.DesiredSize;
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        if (Child is FrameworkElement child) child.Arrange(new Rect(arrangeRect.Position, arrangeRect.Size));
    }

    private void UpdatePlacement()
    {
        if (!IsOpen) return;
        PopupService.HidePopup(this);
        PopupService.ShowPopup(this, GetPlacementOffset(), PlacementTarget);
        ActualPlacementChanged?.Invoke(this, null);
    }

    private Vector2 GetPlacementOffset()
    {
        var offset = new Vector2((float)HorizontalOffset, (float)VerticalOffset);
        if (PlacementTarget == null) return offset;
        var origin = Vector2.Transform(Vector2.Zero, PlacementTarget.GetGlobalTransformMatrix());
        return DesiredPlacement switch
        {
            PopupPlacementMode.Right or PopupPlacementMode.RightEdgeAlignedTop or PopupPlacementMode.RightEdgeAlignedBottom => origin + new Vector2(PlacementTarget.Size.X, 0f) + offset,
            PopupPlacementMode.Bottom or PopupPlacementMode.BottomEdgeAlignedLeft or PopupPlacementMode.BottomEdgeAlignedRight => origin + new Vector2(0f, PlacementTarget.Size.Y) + offset,
            _ => origin + offset
        };
    }

    private static void OnChildChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var popup = (Popup)dependencyObject;
        if (args.OldValue is UIElement oldChild) popup.RemoveChild(oldChild);
        if (args.NewValue is UIElement newChild) popup.AddChild(newChild);
        popup.InvalidateMeasure();
        popup.Invalidate();
    }

    private static void OnIsOpenChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var popup = (Popup)dependencyObject;
        if ((bool)(args.NewValue ?? false))
        {
            PopupService.ShowPopup(popup, popup.GetPlacementOffset(), popup.PlacementTarget);
            popup.Opened?.Invoke(popup, null);
        }
        else
        {
            PopupService.HidePopup(popup);
            popup.Closed?.Invoke(popup, null);
        }
    }

    private static void OnPlacementChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) =>
        ((Popup)dependencyObject).UpdatePlacement();

    private T Get<T>(DependencyProperty property) => (T)GetValue(property)!;
    private static DependencyProperty Register<T>(string name, T defaultValue, PropertyChangedCallback? callback = null) =>
        DependencyProperty.Register(name, typeof(T), typeof(Popup),
            new PropertyMetadata(defaultValue, callback) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });
}
