using Microsoft.UI.Xaml;
using System.Numerics;

namespace Microsoft.UI.Xaml.Media;

public abstract class Transform : DependencyObject
{
    public abstract Matrix4x4 Value { get; }
}
