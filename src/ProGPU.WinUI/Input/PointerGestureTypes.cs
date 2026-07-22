using System.Numerics;
using ProGPU.Scene;

namespace Windows.Devices.Input
{
    public enum PointerDeviceType
    {
        Touch = 0,
        Pen = 1,
        Mouse = 2
    }

    public sealed class PointerDevice
    {
        internal PointerDevice(PointerDeviceType pointerDeviceType)
        {
            PointerDeviceType = pointerDeviceType;
        }

        public PointerDeviceType PointerDeviceType { get; }

        public static PointerDevice GetPointerDevice(PointerDeviceType pointerDeviceType) => new(pointerDeviceType);
    }
}

namespace Microsoft.UI.Input
{
using Windows.Devices.Input;

public sealed class PointerPointProperties
{
    internal PointerPointProperties()
    {
    }

    public Windows.Foundation.Rect ContactRect { get; internal set; }
    public bool IsBarrelButtonPressed { get; internal set; }
    public bool IsHorizontalMouseWheel { get; internal set; }
    public bool IsInRange { get; internal set; } = true;
    public bool IsInverted { get; internal set; }
    public bool IsLeftButtonPressed { get; internal set; }
    public bool IsMiddleButtonPressed { get; internal set; }
    public bool IsRightButtonPressed { get; internal set; }
    public bool IsXButton1Pressed { get; internal set; }
    public bool IsXButton2Pressed { get; internal set; }
    public bool IsPrimary { get; internal set; }
    public bool IsCanceled { get; internal set; }
    public bool IsEraser { get; internal set; }
    public float Orientation { get; internal set; }
    public PointerUpdateKind PointerUpdateKind { get; internal set; }
    public float Pressure { get; internal set; }
    public bool TouchConfidence { get; internal set; } = true;
    public float Twist { get; internal set; }
    public float XTilt { get; internal set; }
    public float YTilt { get; internal set; }
    public int MouseWheelDelta { get; internal set; }
}

public sealed class PointerPoint
{
    internal PointerPoint(
        uint pointerId,
        ulong timestamp,
        Vector2 position,
        Vector2 rawPosition,
        Windows.Devices.Input.PointerDeviceType deviceType,
        bool isInContact,
        PointerPointProperties properties)
        : this(pointerId, timestamp, position, rawPosition, deviceType, deviceType switch
        {
            Windows.Devices.Input.PointerDeviceType.Touch => Microsoft.UI.Input.PointerDeviceType.Touch,
            Windows.Devices.Input.PointerDeviceType.Pen => Microsoft.UI.Input.PointerDeviceType.Pen,
            _ => Microsoft.UI.Input.PointerDeviceType.Mouse
        }, isInContact, properties)
    {
    }

    private PointerPoint(
        uint pointerId,
        ulong timestamp,
        Vector2 position,
        Vector2 rawPosition,
        Windows.Devices.Input.PointerDeviceType legacyDeviceType,
        Microsoft.UI.Input.PointerDeviceType deviceType,
        bool isInContact,
        PointerPointProperties properties)
    {
        PointerId = pointerId;
        Timestamp = timestamp;
        FrameId = unchecked((uint)timestamp);
        Position = new Windows.Foundation.Point(position.X, position.Y);
        RawPosition = rawPosition;
        PointerDevice = Windows.Devices.Input.PointerDevice.GetPointerDevice(legacyDeviceType);
        PointerDeviceType = deviceType;
        IsInContact = isInContact;
        Properties = properties;
    }

    public uint FrameId { get; }
    public uint PointerId { get; }
    public ulong Timestamp { get; }
    public Windows.Foundation.Point Position { get; }
    internal Vector2 RawPosition { get; }
    public Microsoft.UI.Input.PointerDeviceType PointerDeviceType { get; }
    // Kept as a source-compatibility extension for existing ProGPU XAML code.
    public Windows.Devices.Input.PointerDevice PointerDevice { get; }
    public bool IsInContact { get; }
    public PointerPointProperties Properties { get; }

    public PointerPoint? GetTransformedPoint(IPointerPointTransform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (!transform.TryTransform(Position, out var transformedPosition) ||
            !transform.TryTransformBounds(Properties.ContactRect, out var transformedContactRect)) return null;
        var transformedProperties = new PointerPointProperties
        {
            ContactRect = transformedContactRect,
            IsBarrelButtonPressed = Properties.IsBarrelButtonPressed,
            IsHorizontalMouseWheel = Properties.IsHorizontalMouseWheel,
            IsInRange = Properties.IsInRange,
            IsInverted = Properties.IsInverted,
            IsLeftButtonPressed = Properties.IsLeftButtonPressed,
            IsMiddleButtonPressed = Properties.IsMiddleButtonPressed,
            IsRightButtonPressed = Properties.IsRightButtonPressed,
            IsXButton1Pressed = Properties.IsXButton1Pressed,
            IsXButton2Pressed = Properties.IsXButton2Pressed,
            IsPrimary = Properties.IsPrimary,
            IsCanceled = Properties.IsCanceled,
            IsEraser = Properties.IsEraser,
            Orientation = Properties.Orientation,
            PointerUpdateKind = Properties.PointerUpdateKind,
            Pressure = Properties.Pressure,
            TouchConfidence = Properties.TouchConfidence,
            Twist = Properties.Twist,
            XTilt = Properties.XTilt,
            YTilt = Properties.YTilt,
            MouseWheelDelta = Properties.MouseWheelDelta
        };
        var transformed = new Vector2((float)transformedPosition.X, (float)transformedPosition.Y);
        return new PointerPoint(PointerId, Timestamp, transformed, transformed,
            PointerDevice.PointerDeviceType, PointerDeviceType, IsInContact, transformedProperties);
    }
}

public interface IPointerPointTransform
{
    IPointerPointTransform Inverse { get; }
    bool TryTransform(Windows.Foundation.Point inPoint, out Windows.Foundation.Point outPoint);
    bool TryTransformBounds(Windows.Foundation.Rect inRect, out Windows.Foundation.Rect outRect);
}

public enum PointerDeviceType
{
    Touch = 0,
    Pen = 1,
    Mouse = 2,
    Touchpad = 3
}

public enum PointerUpdateKind
{
    Other = 0,
    LeftButtonPressed = 1,
    LeftButtonReleased = 2,
    RightButtonPressed = 3,
    RightButtonReleased = 4,
    MiddleButtonPressed = 5,
    MiddleButtonReleased = 6,
    XButton1Pressed = 7,
    XButton1Released = 8,
    XButton2Pressed = 9,
    XButton2Released = 10
}
}

namespace Microsoft.UI.Xaml.Input
{
using InputPointerDeviceType = Microsoft.UI.Input.PointerDeviceType;
using LegacyPointerDeviceType = Windows.Devices.Input.PointerDeviceType;

public sealed class Pointer
{
    internal Pointer(uint pointerId, LegacyPointerDeviceType pointerDeviceType, bool isInContact, bool isInRange = true)
    {
        PointerId = pointerId;
        LegacyPointerDeviceType = pointerDeviceType;
        PointerDeviceType = pointerDeviceType switch
        {
            LegacyPointerDeviceType.Touch => InputPointerDeviceType.Touch,
            LegacyPointerDeviceType.Pen => InputPointerDeviceType.Pen,
            _ => InputPointerDeviceType.Mouse
        };
        IsInContact = isInContact;
        IsInRange = isInRange;
    }

    public uint PointerId { get; }
    public InputPointerDeviceType PointerDeviceType { get; }
    internal LegacyPointerDeviceType LegacyPointerDeviceType { get; }
    public bool IsInContact { get; internal set; }
    public bool IsInRange { get; internal set; }
}

[Flags]
public enum ManipulationModes : uint
{
    None = 0,
    TranslateX = 1,
    TranslateY = 2,
    TranslateRailsX = 4,
    TranslateRailsY = 8,
    Rotate = 16,
    Scale = 32,
    TranslateInertia = 64,
    RotateInertia = 128,
    ScaleInertia = 256,
    All = 65535,
    System = 65536
}

internal static class GesturePosition
{
    public static Windows.Foundation.Point Get(
        Microsoft.UI.Xaml.UIElement? relativeTo,
        Vector2 screenPosition) =>
        InputSystem.GetLocalPosition(relativeTo as Microsoft.UI.Xaml.FrameworkElement, screenPosition);
}

public sealed class TappedRoutedEventArgs : Microsoft.UI.Xaml.RoutedEventArgs
{
    internal Vector2 ScreenPosition { get; init; }
    public InputPointerDeviceType PointerDeviceType { get; internal init; }
    public Windows.Foundation.Point GetPosition(Microsoft.UI.Xaml.UIElement? relativeTo) =>
        GesturePosition.Get(relativeTo, ScreenPosition);
}

public sealed class DoubleTappedRoutedEventArgs : Microsoft.UI.Xaml.RoutedEventArgs
{
    internal Vector2 ScreenPosition { get; init; }
    public InputPointerDeviceType PointerDeviceType { get; internal init; }
    public Windows.Foundation.Point GetPosition(Microsoft.UI.Xaml.UIElement? relativeTo) =>
        GesturePosition.Get(relativeTo, ScreenPosition);
}

public sealed class RightTappedRoutedEventArgs : Microsoft.UI.Xaml.RoutedEventArgs
{
    internal Vector2 ScreenPosition { get; init; }
    public InputPointerDeviceType PointerDeviceType { get; internal init; }
    public Windows.Foundation.Point GetPosition(Microsoft.UI.Xaml.UIElement? relativeTo) =>
        GesturePosition.Get(relativeTo, ScreenPosition);
}

public sealed class HoldingRoutedEventArgs : Microsoft.UI.Xaml.RoutedEventArgs
{
    internal Vector2 ScreenPosition { get; init; }
    public Microsoft.UI.Input.HoldingState HoldingState { get; internal init; }
    public InputPointerDeviceType PointerDeviceType { get; internal init; }
    public Windows.Foundation.Point GetPosition(Microsoft.UI.Xaml.UIElement? relativeTo) =>
        GesturePosition.Get(relativeTo, ScreenPosition);
}

public sealed class ManipulationPivot
{
    public ManipulationPivot()
    {
    }

    public ManipulationPivot(Windows.Foundation.Point center, double radius)
    {
        Center = center;
        Radius = radius;
    }

    public Windows.Foundation.Point Center { get; set; }
    public double Radius { get; set; }
}

public sealed class InertiaExpansionBehavior
{
    public double DesiredDeceleration { get; set; } = float.NaN;
    public double DesiredExpansion { get; set; } = float.NaN;
}

public sealed class InertiaRotationBehavior
{
    public double DesiredDeceleration { get; set; } = float.NaN;
    public double DesiredRotation { get; set; } = float.NaN;
}

public sealed class InertiaTranslationBehavior
{
    public double DesiredDeceleration { get; set; } = float.NaN;
    public double DesiredDisplacement { get; set; } = float.NaN;
}

public sealed class ManipulationStartingRoutedEventArgs : Microsoft.UI.Xaml.RoutedEventArgs
{
    public ManipulationModes Mode { get; set; } = ManipulationModes.All;
    public Microsoft.UI.Xaml.UIElement? Container { get; set; }
    public ManipulationPivot? Pivot { get; set; }

    // Source-compatible aliases retained for early ProGPU callers.
    public Vector2 PivotCenter
    {
        get => Pivot?.Center ?? default;
        set => (Pivot ??= new ManipulationPivot()).Center = value;
    }
    public float PivotRadius
    {
        get => (float)(Pivot?.Radius ?? 0d);
        set => (Pivot ??= new ManipulationPivot()).Radius = value;
    }
}

public class ManipulationStartedRoutedEventArgs : Microsoft.UI.Xaml.RoutedEventArgs
{
    public Microsoft.UI.Xaml.UIElement? Container { get; internal init; }
    public Microsoft.UI.Input.ManipulationDelta Cumulative { get; internal init; } = Microsoft.UI.Input.ManipulationDelta.Identity;
    public InputPointerDeviceType PointerDeviceType { get; internal init; }
    public Windows.Foundation.Point Position { get; internal init; }
    internal bool IsCompleteRequested { get; private set; }
    public void Complete() => IsCompleteRequested = true;
}

public sealed class ManipulationDeltaRoutedEventArgs : Microsoft.UI.Xaml.RoutedEventArgs
{
    public Microsoft.UI.Xaml.UIElement? Container { get; internal init; }
    public Microsoft.UI.Input.ManipulationDelta Delta { get; internal init; } = Microsoft.UI.Input.ManipulationDelta.Identity;
    public Microsoft.UI.Input.ManipulationDelta Cumulative { get; internal init; } = Microsoft.UI.Input.ManipulationDelta.Identity;
    public Microsoft.UI.Input.ManipulationVelocities Velocities { get; internal init; }
    public bool IsInertial { get; internal init; }
    public InputPointerDeviceType PointerDeviceType { get; internal init; }
    public Windows.Foundation.Point Position { get; internal init; }
    internal bool IsCompleteRequested { get; private set; }
    public void Complete() => IsCompleteRequested = true;
}

public sealed class ManipulationInertiaStartingRoutedEventArgs : Microsoft.UI.Xaml.RoutedEventArgs
{
    public Microsoft.UI.Xaml.UIElement? Container { get; internal init; }
    public Microsoft.UI.Input.ManipulationDelta Cumulative { get; internal init; } = Microsoft.UI.Input.ManipulationDelta.Identity;
    public Microsoft.UI.Input.ManipulationDelta Delta { get; internal init; } = Microsoft.UI.Input.ManipulationDelta.Identity;
    public Microsoft.UI.Input.ManipulationVelocities Velocities { get; internal init; }
    public InertiaExpansionBehavior ExpansionBehavior { get; set; } = new();
    public InertiaRotationBehavior RotationBehavior { get; set; } = new();
    public InertiaTranslationBehavior TranslationBehavior { get; set; } = new();
    public InputPointerDeviceType PointerDeviceType { get; internal init; }

    public float TranslationDeceleration
    {
        get => double.IsNaN(TranslationBehavior.DesiredDeceleration) ? 0f : (float)TranslationBehavior.DesiredDeceleration;
        set => TranslationBehavior.DesiredDeceleration = value;
    }
    public float RotationDeceleration
    {
        get => double.IsNaN(RotationBehavior.DesiredDeceleration) ? 0f : (float)RotationBehavior.DesiredDeceleration;
        set => RotationBehavior.DesiredDeceleration = value;
    }
    public float ExpansionDeceleration
    {
        get => double.IsNaN(ExpansionBehavior.DesiredDeceleration) ? 0f : (float)ExpansionBehavior.DesiredDeceleration;
        set => ExpansionBehavior.DesiredDeceleration = value;
    }
}

public sealed class ManipulationCompletedRoutedEventArgs : Microsoft.UI.Xaml.RoutedEventArgs
{
    public Microsoft.UI.Xaml.UIElement? Container { get; internal init; }
    public Microsoft.UI.Input.ManipulationDelta Cumulative { get; internal init; } = Microsoft.UI.Input.ManipulationDelta.Identity;
    public Microsoft.UI.Input.ManipulationVelocities Velocities { get; internal init; }
    public bool IsInertial { get; internal init; }
    public InputPointerDeviceType PointerDeviceType { get; internal init; }
    public Windows.Foundation.Point Position { get; internal init; }
}

public delegate void TappedEventHandler(object sender, TappedRoutedEventArgs e);
public delegate void DoubleTappedEventHandler(object sender, DoubleTappedRoutedEventArgs e);
public delegate void RightTappedEventHandler(object sender, RightTappedRoutedEventArgs e);
public delegate void HoldingEventHandler(object sender, HoldingRoutedEventArgs e);
public delegate void ManipulationStartingEventHandler(object sender, ManipulationStartingRoutedEventArgs e);
public delegate void ManipulationStartedEventHandler(object sender, ManipulationStartedRoutedEventArgs e);
public delegate void ManipulationDeltaEventHandler(object sender, ManipulationDeltaRoutedEventArgs e);
public delegate void ManipulationInertiaStartingEventHandler(object sender, ManipulationInertiaStartingRoutedEventArgs e);
public delegate void ManipulationCompletedEventHandler(object sender, ManipulationCompletedRoutedEventArgs e);

public enum InputScopeNameValue
{
    Default = 0,
    Url,
    EmailSmtpAddress,
    Number,
    TelephoneNumber,
    Search,
    Chat,
    NameOrPhoneNumber,
    Password,
    NumericPin
}

public sealed class InputScopeName
{
    public InputScopeNameValue NameValue { get; set; }
}

public sealed class InputScope
{
    public IList<InputScopeName> Names { get; } = new List<InputScopeName>();
}

public enum TextInputEventKind
{
    InsertText,
    DeleteContentBackward,
    DeleteContentForward,
    InsertLineBreak,
    CompositionStarted,
    CompositionUpdated,
    CompositionCompleted,
    CompositionCanceled,
    ReplaceText,
    SelectionChanged,
    Paste
}

public sealed class TextInputRoutedEventArgs : Microsoft.UI.Xaml.RoutedEventArgs
{
    public TextInputEventKind Kind { get; internal init; }
    public string Text { get; internal init; } = string.Empty;
    public bool IsComposing { get; internal init; }
    public int ReplacementStart { get; internal init; } = -1;
    public int ReplacementLength { get; internal init; }
    public int SelectionStart { get; internal init; } = -1;
    public int SelectionLength { get; internal init; }
}

public readonly record struct TextInputOptions(
    InputScopeNameValue InputScope,
    string EnterKeyHint,
    string AutoCapitalize,
    bool IsSpellCheckEnabled,
    bool IsPassword,
    bool AcceptsReturn,
    string Text,
    int SelectionStart,
    int SelectionLength,
    Rect Bounds);

public interface ITextInputClient
{
    TextInputOptions GetTextInputOptions();
    void OnTextInput(TextInputRoutedEventArgs args);
}

public enum PointerInputKind
{
    Moved,
    Pressed,
    Released,
    Canceled,
    Wheel
}

public readonly record struct PointerInputEvent(
    PointerInputKind Kind,
    uint PointerId,
    LegacyPointerDeviceType DeviceType,
    Vector2 Position,
    ulong Timestamp,
    bool IsPrimary = true,
    bool IsInContact = false,
    bool IsLeftButtonPressed = false,
    bool IsMiddleButtonPressed = false,
    bool IsRightButtonPressed = false,
    float Pressure = 0f,
    Rect ContactRect = default,
    float WheelDeltaX = 0f,
    float WheelDeltaY = 0f,
    bool IsPreciseWheel = false,
    VirtualKeyModifiers Modifiers = VirtualKeyModifiers.None);
}
