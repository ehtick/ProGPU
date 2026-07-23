using System.Collections.ObjectModel;

namespace Microsoft.UI.Xaml.Media.Animation;

public abstract class Transition : DependencyObject
{
}

public sealed class TransitionCollection : Collection<Transition>
{
}

public sealed class EntranceThemeTransition : Transition
{
    public double FromHorizontalOffset { get; set; }
    public double FromVerticalOffset { get; set; }
    public bool IsStaggeringEnabled { get; set; } = true;
}

public sealed class AddDeleteThemeTransition : Transition
{
}

public sealed class ReorderThemeTransition : Transition
{
}

public sealed class ContentThemeTransition : Transition
{
}
