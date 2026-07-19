using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ProGPU.Scene;
using Windows.Devices.Input;
using Xunit;

namespace ProGPU.Tests;

public sealed class TouchGestureResponsiveTests
{
    [Fact]
    public void TouchPointerPreservesIdentityCaptureAndCancellation()
    {
        var target = new TrackingControl { Width = 120, Height = 120 };
        ArrangeRoot(target, new Vector2(240, 240));
        UseInputRoot(target);

        InputSystem.InjectPointer(Touch(PointerInputKind.Pressed, 17, 20, 20, 1_000, true));
        Assert.Equal((uint)17, target.LastPointerId);
        Assert.Equal(PointerDeviceType.Touch, target.LastDeviceType);
        Assert.True(target.CaptureSucceeded);

        InputSystem.InjectPointer(Touch(PointerInputKind.Moved, 17, 210, 210, 20_000, true));
        Assert.Equal(new Vector2(210, 210), target.LastScreenPosition);

        InputSystem.InjectPointer(Touch(PointerInputKind.Canceled, 17, 210, 210, 30_000, false));
        Assert.Equal(1, target.CanceledCount);
        Assert.Equal(1, target.CaptureLostCount);
    }

    [Fact]
    public void TouchRecognizesTapDoubleTapAndTwoPointerScale()
    {
        var target = new TrackingControl
        {
            Width = 220,
            Height = 220,
            ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY | ManipulationModes.Scale | ManipulationModes.Rotate
        };
        ArrangeRoot(target, new Vector2(240, 240));
        UseInputRoot(target);

        InputSystem.InjectPointer(Touch(PointerInputKind.Pressed, 1, 30, 30, 1_000, true));
        InputSystem.InjectPointer(Touch(PointerInputKind.Released, 1, 30, 30, 60_000, false));
        InputSystem.InjectPointer(Touch(PointerInputKind.Pressed, 2, 31, 30, 200_000, true));
        InputSystem.InjectPointer(Touch(PointerInputKind.Released, 2, 31, 30, 250_000, false));
        Assert.Equal(2, target.TappedCount);
        Assert.Equal(1, target.DoubleTappedCount);

        InputSystem.InjectPointer(Touch(PointerInputKind.Pressed, 3, 60, 80, 1_000_000, true));
        InputSystem.InjectPointer(Touch(PointerInputKind.Pressed, 4, 120, 80, 1_010_000, true));
        InputSystem.InjectPointer(Touch(PointerInputKind.Moved, 4, 180, 80, 1_030_000, true));
        Assert.True(target.ManipulationStartedCount > 0);
        Assert.True(target.LastManipulationScale > 1.5f);
        InputSystem.InjectPointer(Touch(PointerInputKind.Released, 3, 60, 80, 1_050_000, false));
        InputSystem.InjectPointer(Touch(PointerInputKind.Released, 4, 180, 80, 1_060_000, false));
        Assert.Equal(1, target.ManipulationCompletedCount);
    }

    [Fact]
    public void MobileTextOperationsDoNotDependOnPhysicalKeyEvents()
    {
        var textBox = new TextBox();
        ArrangeRoot(textBox, new Vector2(240, 80));
        UseInputRoot(textBox);
        InputSystem.SetFocus(textBox);

        InputSystem.InjectTextInput(TextInputEventKind.InsertText, "A😀");
        InputSystem.InjectTextInput(TextInputEventKind.DeleteContentBackward);
        Assert.Equal("A", textBox.Text);

        InputSystem.InjectTextInput(TextInputEventKind.CompositionStarted, isComposing: true);
        InputSystem.InjectTextInput(TextInputEventKind.CompositionUpdated, "に", true);
        InputSystem.InjectTextInput(TextInputEventKind.CompositionUpdated, "日本", true);
        InputSystem.InjectTextInput(TextInputEventKind.CompositionCompleted, "日本");
        Assert.Equal("A日本", textBox.Text);

        textBox.SelectionStart = 1;
        textBox.SelectionLength = 2;
        textBox.CaretIndex = 3;
        InputSystem.InjectTextInput(TextInputEventKind.CompositionStarted, isComposing: true);
        InputSystem.InjectTextInput(TextInputEventKind.CompositionUpdated, "に", true);
        InputSystem.InjectTextInput(TextInputEventKind.CompositionCanceled);
        Assert.Equal("A日本", textBox.Text);
    }

    [Fact]
    public void ScrollViewerConsumesTouchManipulationAndSupportsChangeView()
    {
        var content = new Border
        {
            Height = 900,
            Background = new ProGPU.Vector.ThemeResourceBrush("ControlBackground")
        };
        var viewer = new ScrollViewer { Content = content, Width = 220, Height = 180 };
        ArrangeRoot(viewer, new Vector2(220, 180));
        UseInputRoot(viewer);

        InputSystem.InjectPointer(Touch(PointerInputKind.Pressed, 8, 100, 130, 1_000, true));
        InputSystem.InjectPointer(Touch(PointerInputKind.Moved, 8, 100, 60, 30_000, true));
        Assert.True(viewer.VerticalOffset >= 60f);
        InputSystem.InjectPointer(Touch(PointerInputKind.Released, 8, 100, 60, 40_000, false));

        Assert.True(viewer.ChangeView(null, 200, null));
        Assert.Equal(200f, viewer.VerticalOffset);
    }

    [Fact]
    public void VisualStatesRestoreSettersAndNavigationViewUsesWinUiBreakpoints()
    {
        var control = new Button();
        var group = new VisualStateGroup { Name = "States" };
        var compact = new VisualState { Name = "Compact" };
        compact.Setters.Add(new Setter(nameof(FrameworkElement.Visibility), Visibility.Collapsed));
        var normal = new VisualState { Name = "Normal" };
        group.States.Add(compact);
        group.States.Add(normal);
        VisualStateManager.GetVisualStateGroups(control).Add(group);

        Assert.True(VisualStateManager.GoToState(control, "Compact", false));
        Assert.Equal(Visibility.Collapsed, control.Visibility);
        Assert.True(VisualStateManager.GoToState(control, "Normal", false));
        Assert.Equal(Visibility.Visible, control.Visibility);

        var navigation = new NavigationView();
        navigation.Measure(new Vector2(500, 500));
        Assert.Equal(NavigationViewDisplayMode.Minimal, navigation.DisplayMode);
        navigation.Measure(new Vector2(800, 500));
        Assert.Equal(NavigationViewDisplayMode.Compact, navigation.DisplayMode);
        navigation.Measure(new Vector2(1200, 500));
        Assert.Equal(NavigationViewDisplayMode.Expanded, navigation.DisplayMode);
    }

    private static PointerInputEvent Touch(PointerInputKind kind, uint id, float x, float y, ulong timestamp, bool contact) => new(
        kind,
        id,
        PointerDeviceType.Touch,
        new Vector2(x, y),
        timestamp,
        IsPrimary: id == 1,
        IsInContact: contact,
        IsLeftButtonPressed: contact,
        Pressure: contact ? 0.6f : 0f,
        ContactRect: new Rect(x - 5, y - 5, 10, 10));

    private static void ArrangeRoot(FrameworkElement root, Vector2 size)
    {
        root.Measure(size);
        root.Arrange(new Rect(0, 0, size.X, size.Y));
    }

    private static void UseInputRoot(FrameworkElement root)
    {
        InputSystem.Current = InputSystem.CreateExternalState(root);
        InputSystem.Root = root;
    }

    private sealed class TrackingControl : Control
    {
        public uint LastPointerId { get; private set; }
        public PointerDeviceType LastDeviceType { get; private set; }
        public Vector2 LastScreenPosition { get; private set; }
        public bool CaptureSucceeded { get; private set; }
        public int CanceledCount { get; private set; }
        public int CaptureLostCount { get; private set; }
        public int TappedCount { get; private set; }
        public int DoubleTappedCount { get; private set; }
        public int ManipulationStartedCount { get; private set; }
        public int ManipulationCompletedCount { get; private set; }
        public float LastManipulationScale { get; private set; } = 1f;

        public override void OnPointerPressed(PointerRoutedEventArgs e)
        {
            LastPointerId = e.Pointer.PointerId;
            LastDeviceType = e.Pointer.PointerDeviceType;
            CaptureSucceeded = CapturePointer(e.Pointer);
            base.OnPointerPressed(e);
        }

        public override void OnPointerMoved(PointerRoutedEventArgs e)
        {
            LastScreenPosition = e.ScreenPosition;
            base.OnPointerMoved(e);
        }

        public override void OnPointerCanceled(PointerRoutedEventArgs e)
        {
            CanceledCount++;
            base.OnPointerCanceled(e);
        }

        public override void OnPointerCaptureLost(PointerRoutedEventArgs e)
        {
            CaptureLostCount++;
            base.OnPointerCaptureLost(e);
        }

        public override void OnTapped(TappedRoutedEventArgs e)
        {
            TappedCount++;
            base.OnTapped(e);
        }

        public override void OnDoubleTapped(DoubleTappedRoutedEventArgs e)
        {
            DoubleTappedCount++;
            base.OnDoubleTapped(e);
        }

        public override void OnManipulationStarted(ManipulationStartedRoutedEventArgs e)
        {
            ManipulationStartedCount++;
            base.OnManipulationStarted(e);
        }

        public override void OnManipulationDelta(ManipulationDeltaRoutedEventArgs e)
        {
            LastManipulationScale = e.Delta.Scale;
            base.OnManipulationDelta(e);
        }

        public override void OnManipulationCompleted(ManipulationCompletedRoutedEventArgs e)
        {
            ManipulationCompletedCount++;
            base.OnManipulationCompleted(e);
        }
    }
}
