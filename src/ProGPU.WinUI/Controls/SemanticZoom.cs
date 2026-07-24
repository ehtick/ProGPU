using System.Numerics;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Controls.Primitives;
using ProGPU.Layout;
using ProGPU.Scene;

namespace Microsoft.UI.Xaml.Controls;

public sealed class SemanticZoomLocation
{
    public Windows.Foundation.Rect Bounds { get; set; }
    public object? Item { get; set; }
}

public interface ISemanticZoomInformation
{
    bool IsActiveView { get; set; }
    bool IsZoomedInView { get; set; }
    SemanticZoom? SemanticZoomOwner { get; set; }
    void CompleteViewChange();
    void CompleteViewChangeFrom(SemanticZoomLocation source, SemanticZoomLocation destination);
    void CompleteViewChangeTo(SemanticZoomLocation source, SemanticZoomLocation destination);
    void InitializeViewChange();
    void MakeVisible(SemanticZoomLocation item);
    void StartViewChangeFrom(SemanticZoomLocation source, SemanticZoomLocation destination);
    void StartViewChangeTo(SemanticZoomLocation source, SemanticZoomLocation destination);
}

public sealed class SemanticZoomViewChangedEventArgs : EventArgs
{
    public SemanticZoomLocation? SourceItem { get; set; }
    public SemanticZoomLocation? DestinationItem { get; set; }
    public bool IsSourceZoomedInView { get; set; }
}

[ContentProperty(Name = nameof(ZoomedInView))]
public sealed class SemanticZoom : Control
{
    public static readonly DependencyProperty ZoomedInViewProperty = DependencyProperty.Register(
        nameof(ZoomedInView), typeof(ISemanticZoomInformation), typeof(SemanticZoom),
        new PropertyMetadata(null, static (owner, args) => ((SemanticZoom)owner).OnViewChanged(
            args.OldValue as ISemanticZoomInformation,
            args.NewValue as ISemanticZoomInformation,
            isZoomedIn: true)) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public static readonly DependencyProperty ZoomedOutViewProperty = DependencyProperty.Register(
        nameof(ZoomedOutView), typeof(ISemanticZoomInformation), typeof(SemanticZoom),
        new PropertyMetadata(null, static (owner, args) => ((SemanticZoom)owner).OnViewChanged(
            args.OldValue as ISemanticZoomInformation,
            args.NewValue as ISemanticZoomInformation,
            isZoomedIn: false)) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public static readonly DependencyProperty IsZoomedInViewActiveProperty = DependencyProperty.Register(
        nameof(IsZoomedInViewActive), typeof(bool), typeof(SemanticZoom),
        new PropertyMetadata(true, static (owner, args) => ((SemanticZoom)owner).SwitchView(
            (bool)(args.OldValue ?? true), (bool)(args.NewValue ?? true)))
        { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public ISemanticZoomInformation? ZoomedInView
    {
        get => GetValue(ZoomedInViewProperty) as ISemanticZoomInformation;
        set => SetValue(ZoomedInViewProperty, value);
    }

    public ISemanticZoomInformation? ZoomedOutView
    {
        get => GetValue(ZoomedOutViewProperty) as ISemanticZoomInformation;
        set => SetValue(ZoomedOutViewProperty, value);
    }

    public bool IsZoomedInViewActive
    {
        get => (bool)(GetValue(IsZoomedInViewActiveProperty) ?? true);
        set => SetValue(IsZoomedInViewActiveProperty, value);
    }

    public event EventHandler<SemanticZoomViewChangedEventArgs>? ViewChangeStarted;
    public event EventHandler<SemanticZoomViewChangedEventArgs>? ViewChangeCompleted;

    private ISemanticZoomInformation? ActiveView => IsZoomedInViewActive ? ZoomedInView : ZoomedOutView;

    private void OnViewChanged(ISemanticZoomInformation? oldView, ISemanticZoomInformation? newView, bool isZoomedIn)
    {
        if (oldView != null)
        {
            oldView.IsActiveView = false;
            oldView.SemanticZoomOwner = null;
            if (oldView is FrameworkElement oldElement && ReferenceEquals(oldElement.Parent, this)) RemoveChild(oldElement);
        }

        if (newView != null)
        {
            newView.SemanticZoomOwner = this;
            newView.IsZoomedInView = isZoomedIn;
            newView.IsActiveView = IsZoomedInViewActive == isZoomedIn;
            if (newView.IsActiveView && newView is FrameworkElement newElement) AddChild(newElement);
        }
    }

    private void SwitchView(bool oldValue, bool newValue)
    {
        if (oldValue == newValue) return;
        var source = oldValue ? ZoomedInView : ZoomedOutView;
        var destination = newValue ? ZoomedInView : ZoomedOutView;
        var sourceLocation = new SemanticZoomLocation { Item = (source as Selector)?.SelectedItem };
        var destinationLocation = new SemanticZoomLocation { Item = sourceLocation.Item };
        var args = new SemanticZoomViewChangedEventArgs
        {
            IsSourceZoomedInView = oldValue,
            SourceItem = sourceLocation,
            DestinationItem = destinationLocation
        };

        source?.InitializeViewChange();
        destination?.InitializeViewChange();
        source?.StartViewChangeFrom(sourceLocation, destinationLocation);
        destination?.StartViewChangeTo(sourceLocation, destinationLocation);
        ViewChangeStarted?.Invoke(this, args);

        if (source != null) source.IsActiveView = false;
        if (source is FrameworkElement sourceElement && ReferenceEquals(sourceElement.Parent, this))
            RemoveChild(sourceElement);
        if (destination != null)
        {
            destination.IsActiveView = true;
            if (destination is FrameworkElement destinationElement && !ReferenceEquals(destinationElement.Parent, this))
                AddChild(destinationElement);
            destination.MakeVisible(destinationLocation);
        }

        source?.CompleteViewChangeFrom(sourceLocation, destinationLocation);
        destination?.CompleteViewChangeTo(sourceLocation, destinationLocation);
        source?.CompleteViewChange();
        destination?.CompleteViewChange();
        ViewChangeCompleted?.Invoke(this, args);
        InvalidateMeasure();
        Invalidate();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (ActiveView is FrameworkElement element)
        {
            element.Measure(availableSize);
            return element.DesiredSize;
        }
        return Vector2.Zero;
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        if (ActiveView is FrameworkElement element)
            element.Arrange(arrangeRect);
    }
}
