using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;

namespace Microsoft.UI.Xaml.Controls;

public class ControlTemplate
{
    public Type TargetType { get; set; }
    public Func<Control, FrameworkElement> Factory { get; set; }

    public ControlTemplate(Type targetType, Func<Control, FrameworkElement> factory)
    {
        TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }
}
