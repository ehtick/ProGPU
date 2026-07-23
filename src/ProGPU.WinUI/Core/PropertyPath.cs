namespace Microsoft.UI.Xaml;

public sealed class PropertyPath : DependencyObject
{
    public PropertyPath(string path) => Path = path ?? throw new ArgumentNullException(nameof(path));

    public string Path { get; }

    public override string ToString() => Path;
}

public sealed class TargetPropertyPath
{
    public TargetPropertyPath()
    {
    }

    public TargetPropertyPath(DependencyProperty targetProperty)
    {
        ArgumentNullException.ThrowIfNull(targetProperty);
        Path = new PropertyPath(targetProperty.Name);
    }

    public PropertyPath? Path { get; set; }
    public object? Target { get; set; }

    public override string ToString() => Path?.Path ?? string.Empty;
}
