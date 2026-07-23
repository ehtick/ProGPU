using System.Collections.ObjectModel;

namespace Microsoft.UI.Xaml.Controls;

public sealed class ColumnDefinition : DependencyObject
{
    public GridLength Width { get; set; } = GridLength.Star();
    public double MinWidth { get; set; }
    public double MaxWidth { get; set; } = double.PositiveInfinity;
    public double ActualWidth { get; internal set; }

    internal float Value => Width.Value;
    internal GridUnitType UnitType => Width.UnitType;

    public static implicit operator ColumnDefinition(GridLength width) => new() { Width = width };
}

public sealed class RowDefinition : DependencyObject
{
    public GridLength Height { get; set; } = GridLength.Star();
    public double MinHeight { get; set; }
    public double MaxHeight { get; set; } = double.PositiveInfinity;
    public double ActualHeight { get; internal set; }

    internal float Value => Height.Value;
    internal GridUnitType UnitType => Height.UnitType;

    public static implicit operator RowDefinition(GridLength height) => new() { Height = height };
}

public sealed class ColumnDefinitionCollection : Collection<ColumnDefinition>
{
}

public sealed class RowDefinitionCollection : Collection<RowDefinition>
{
}
