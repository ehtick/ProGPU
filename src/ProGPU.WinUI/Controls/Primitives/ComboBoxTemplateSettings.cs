namespace Microsoft.UI.Xaml.Controls.Primitives;

public sealed class ComboBoxTemplateSettings : DependencyObject
{
    public static readonly DependencyProperty DropDownOpenedHeightProperty = Register(
        nameof(DropDownOpenedHeight),
        typeof(double),
        0d);

    public static readonly DependencyProperty DropDownClosedHeightProperty = Register(
        nameof(DropDownClosedHeight),
        typeof(double),
        0d);

    public static readonly DependencyProperty DropDownOffsetProperty = Register(
        nameof(DropDownOffset),
        typeof(double),
        0d);

    public static readonly DependencyProperty SelectedItemDirectionProperty = Register(
        nameof(SelectedItemDirection),
        typeof(AnimationDirection),
        AnimationDirection.Top);

    public static readonly DependencyProperty DropDownContentMinWidthProperty = Register(
        nameof(DropDownContentMinWidth),
        typeof(double),
        0d);

    public double DropDownOpenedHeight =>
        (double)(GetValue(DropDownOpenedHeightProperty) ?? 0d);

    public double DropDownClosedHeight =>
        (double)(GetValue(DropDownClosedHeightProperty) ?? 0d);

    public double DropDownOffset =>
        (double)(GetValue(DropDownOffsetProperty) ?? 0d);

    public AnimationDirection SelectedItemDirection =>
        (AnimationDirection)(
            GetValue(SelectedItemDirectionProperty) ??
            AnimationDirection.Top);

    public double DropDownContentMinWidth =>
        (double)(GetValue(DropDownContentMinWidthProperty) ?? 0d);

    internal void Update(float width, float openedHeight, float headerHeight)
    {
        SetValue(DropDownContentMinWidthProperty, (double)Math.Max(0f, width));
        SetValue(DropDownOpenedHeightProperty, (double)Math.Max(0f, openedHeight));
        SetValue(DropDownClosedHeightProperty, (double)Math.Max(0f, headerHeight));
        SetValue(DropDownOffsetProperty, (double)Math.Max(0f, headerHeight));
    }

    private static DependencyProperty Register(
        string name,
        Type propertyType,
        object defaultValue) =>
        DependencyProperty.Register(
            name,
            propertyType,
            typeof(ComboBoxTemplateSettings),
            new PropertyMetadata(defaultValue));
}
