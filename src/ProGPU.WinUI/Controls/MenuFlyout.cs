using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;

namespace Microsoft.UI.Xaml.Controls;

[ContentProperty(Name = nameof(Items))]
public class MenuFlyout : FlyoutBase
{
    public static readonly DependencyProperty MenuFlyoutPresenterStyleProperty = DependencyProperty.Register(
        nameof(MenuFlyoutPresenterStyle), typeof(Style), typeof(MenuFlyout), new PropertyMetadata(null));

    public ObservableCollection<MenuFlyoutItemBase> Items { get; } = new();

    public Style? MenuFlyoutPresenterStyle
    {
        get => GetValue(MenuFlyoutPresenterStyleProperty) as Style;
        set => SetValue(MenuFlyoutPresenterStyleProperty, value);
    }

    protected override Control CreatePresenter()
    {
        var presenter = new MenuFlyoutPresenter { Style = MenuFlyoutPresenterStyle };
        for (var index = 0; index < Items.Count; index++)
        {
            presenter.Items.Add(Items[index]);
        }
        for (var index = 0; index < Items.Count; index++)
        {
            presenter.ItemsHost?.Children.Add(Items[index]);
        }
        return presenter;
    }
}

public class MenuFlyoutItemBase : Control
{
}

[ContentProperty(Name = nameof(Text))]
public class MenuFlyoutItem : MenuFlyoutItemBase
{
    public static readonly DependencyProperty TextProperty = Register<string?>(nameof(Text), null);
    public static readonly DependencyProperty CommandProperty = Register<ICommand?>(nameof(Command), null);
    public static readonly DependencyProperty CommandParameterProperty = Register<object?>(nameof(CommandParameter), null);
    public static readonly DependencyProperty IconProperty = Register<IconElement?>(nameof(Icon), null);
    public static readonly DependencyProperty KeyboardAcceleratorTextOverrideProperty = Register<string?>(nameof(KeyboardAcceleratorTextOverride), null);

    public string? Text { get => GetValue(TextProperty) as string; set => SetValue(TextProperty, value); }
    public ICommand? Command { get => GetValue(CommandProperty) as ICommand; set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
    public IconElement? Icon { get => GetValue(IconProperty) as IconElement; set => SetValue(IconProperty, value); }
    public string? KeyboardAcceleratorTextOverride { get => GetValue(KeyboardAcceleratorTextOverrideProperty) as string; set => SetValue(KeyboardAcceleratorTextOverrideProperty, value); }
    public MenuFlyoutItemTemplateSettings TemplateSettings { get; } = new();

    public event RoutedEventHandler? Click;

    public override void OnPointerReleased(PointerRoutedEventArgs args)
    {
        if (IsEnabled && IsPointerPressed && IsPointerOver) Invoke();
        base.OnPointerReleased(args);
    }

    protected virtual void Invoke()
    {
        if (Command?.CanExecute(CommandParameter) == true) Command.Execute(CommandParameter);
        Click?.Invoke(this, new RoutedEventArgs { OriginalSource = this });
    }

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(MenuFlyoutItem),
            new PropertyMetadata(defaultValue) { AffectsMeasure = true, AffectsRender = true });
}

public class ToggleMenuFlyoutItem : MenuFlyoutItem
{
    public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(
        nameof(IsChecked), typeof(bool), typeof(ToggleMenuFlyoutItem),
        new PropertyMetadata(false) { AffectsRender = true });

    public bool IsChecked { get => (bool)(GetValue(IsCheckedProperty) ?? false); set => SetValue(IsCheckedProperty, value); }

    protected override void Invoke()
    {
        IsChecked = !IsChecked;
        base.Invoke();
    }
}

public class MenuFlyoutSeparator : MenuFlyoutItemBase
{
}

[ContentProperty(Name = nameof(Items))]
public sealed class MenuFlyoutSubItem : MenuFlyoutItemBase
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MenuFlyoutSubItem), new PropertyMetadata(null));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(IconElement), typeof(MenuFlyoutSubItem), new PropertyMetadata(null));

    public ObservableCollection<MenuFlyoutItemBase> Items { get; } = new();
    public string? Text { get => GetValue(TextProperty) as string; set => SetValue(TextProperty, value); }
    public IconElement? Icon { get => GetValue(IconProperty) as IconElement; set => SetValue(IconProperty, value); }
}

public class MenuFlyoutPresenter : ItemsControl
{
    public static readonly DependencyProperty IsDefaultShadowEnabledProperty = DependencyProperty.Register(
        nameof(IsDefaultShadowEnabled), typeof(bool), typeof(MenuFlyoutPresenter), new PropertyMetadata(true));

    public MenuFlyoutPresenterTemplateSettings TemplateSettings { get; } = new();
    public bool IsDefaultShadowEnabled { get => (bool)(GetValue(IsDefaultShadowEnabledProperty) ?? true); set => SetValue(IsDefaultShadowEnabledProperty, value); }
}
