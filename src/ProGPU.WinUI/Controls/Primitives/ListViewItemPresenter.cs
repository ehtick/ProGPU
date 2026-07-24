using System.Numerics;
using ProGPU.Layout;
using ProGPU.Scene;
using ProGPU.Vector;

namespace Microsoft.UI.Xaml.Controls.Primitives;

public enum ListViewItemPresenterCheckMode
{
    Inline = 0,
    Overlay = 1
}

public enum ListViewItemPresenterSelectionIndicatorMode
{
    Inline = 0,
    Overlay = 1
}

public sealed class ListViewItemTemplateSettings : DependencyObject
{
    public int DragItemsCount { get; internal set; }
}

public sealed class GridViewItemTemplateSettings : DependencyObject
{
    public int DragItemsCount { get; internal set; }
}

/// <summary>Theme-aware item chrome used by ListView and GridView item templates.</summary>
public class ListViewItemPresenter : ContentPresenter
{
    public static readonly DependencyProperty SelectionCheckMarkVisualEnabledProperty = Register(nameof(SelectionCheckMarkVisualEnabled), true);
    public static readonly DependencyProperty CheckHintBrushProperty = Register<Brush?>(nameof(CheckHintBrush), null);
    public static readonly DependencyProperty CheckSelectingBrushProperty = Register<Brush?>(nameof(CheckSelectingBrush), null);
    public static readonly DependencyProperty CheckBrushProperty = Register<Brush?>(nameof(CheckBrush), null);
    public static readonly DependencyProperty DragBackgroundProperty = Register<Brush?>(nameof(DragBackground), null);
    public static readonly DependencyProperty DragForegroundProperty = Register<Brush?>(nameof(DragForeground), null);
    public static readonly DependencyProperty FocusBorderBrushProperty = Register<Brush?>(nameof(FocusBorderBrush), null);
    public static readonly DependencyProperty PlaceholderBackgroundProperty = Register<Brush?>(nameof(PlaceholderBackground), null);
    public static readonly DependencyProperty PointerOverBackgroundProperty = Register<Brush?>(nameof(PointerOverBackground), null);
    public static readonly DependencyProperty SelectedBackgroundProperty = Register<Brush?>(nameof(SelectedBackground), null);
    public static readonly DependencyProperty SelectedForegroundProperty = Register<Brush?>(nameof(SelectedForeground), null);
    public static readonly DependencyProperty SelectedPointerOverBackgroundProperty = Register<Brush?>(nameof(SelectedPointerOverBackground), null);
    public static readonly DependencyProperty SelectedPointerOverBorderBrushProperty = Register<Brush?>(nameof(SelectedPointerOverBorderBrush), null);
    public static readonly DependencyProperty SelectedBorderThicknessProperty = Register(nameof(SelectedBorderThickness), default(Thickness));
    public static readonly DependencyProperty DisabledOpacityProperty = Register(nameof(DisabledOpacity), 0.55d);
    public static readonly DependencyProperty DragOpacityProperty = Register(nameof(DragOpacity), 0.7d);
    public static readonly DependencyProperty ReorderHintOffsetProperty = Register(nameof(ReorderHintOffset), 0d);
    public static readonly DependencyProperty PointerOverBackgroundMarginProperty = Register(nameof(PointerOverBackgroundMargin), default(Thickness));
    public static readonly DependencyProperty ContentMarginProperty = Register(nameof(ContentMargin), default(Thickness));
    public static readonly DependencyProperty SelectedPressedBackgroundProperty = Register<Brush?>(nameof(SelectedPressedBackground), null);
    public static readonly DependencyProperty PressedBackgroundProperty = Register<Brush?>(nameof(PressedBackground), null);
    public static readonly DependencyProperty CheckBoxBrushProperty = Register<Brush?>(nameof(CheckBoxBrush), null);
    public static readonly DependencyProperty FocusSecondaryBorderBrushProperty = Register<Brush?>(nameof(FocusSecondaryBorderBrush), null);
    public static readonly DependencyProperty CheckModeProperty = Register(nameof(CheckMode), ListViewItemPresenterCheckMode.Inline);
    public static readonly DependencyProperty PointerOverForegroundProperty = Register<Brush?>(nameof(PointerOverForeground), null);
    public static readonly DependencyProperty RevealBackgroundProperty = Register<Brush?>(nameof(RevealBackground), null);
    public static readonly DependencyProperty RevealBorderBrushProperty = Register<Brush?>(nameof(RevealBorderBrush), null);
    public static readonly DependencyProperty RevealBorderThicknessProperty = Register(nameof(RevealBorderThickness), default(Thickness));
    public static readonly DependencyProperty RevealBackgroundShowsAboveContentProperty = Register(nameof(RevealBackgroundShowsAboveContent), false);
    public static readonly DependencyProperty SelectedDisabledBackgroundProperty = Register<Brush?>(nameof(SelectedDisabledBackground), null);
    public static readonly DependencyProperty CheckPressedBrushProperty = Register<Brush?>(nameof(CheckPressedBrush), null);
    public static readonly DependencyProperty CheckDisabledBrushProperty = Register<Brush?>(nameof(CheckDisabledBrush), null);
    public static readonly DependencyProperty CheckBoxPointerOverBrushProperty = Register<Brush?>(nameof(CheckBoxPointerOverBrush), null);
    public static readonly DependencyProperty CheckBoxPressedBrushProperty = Register<Brush?>(nameof(CheckBoxPressedBrush), null);
    public static readonly DependencyProperty CheckBoxDisabledBrushProperty = Register<Brush?>(nameof(CheckBoxDisabledBrush), null);
    public static readonly DependencyProperty CheckBoxSelectedBrushProperty = Register<Brush?>(nameof(CheckBoxSelectedBrush), null);
    public static readonly DependencyProperty CheckBoxSelectedPointerOverBrushProperty = Register<Brush?>(nameof(CheckBoxSelectedPointerOverBrush), null);
    public static readonly DependencyProperty CheckBoxSelectedPressedBrushProperty = Register<Brush?>(nameof(CheckBoxSelectedPressedBrush), null);
    public static readonly DependencyProperty CheckBoxSelectedDisabledBrushProperty = Register<Brush?>(nameof(CheckBoxSelectedDisabledBrush), null);
    public static readonly DependencyProperty CheckBoxBorderBrushProperty = Register<Brush?>(nameof(CheckBoxBorderBrush), null);
    public static readonly DependencyProperty CheckBoxPointerOverBorderBrushProperty = Register<Brush?>(nameof(CheckBoxPointerOverBorderBrush), null);
    public static readonly DependencyProperty CheckBoxPressedBorderBrushProperty = Register<Brush?>(nameof(CheckBoxPressedBorderBrush), null);
    public static readonly DependencyProperty CheckBoxDisabledBorderBrushProperty = Register<Brush?>(nameof(CheckBoxDisabledBorderBrush), null);
    public static readonly DependencyProperty CheckBoxCornerRadiusProperty = Register(nameof(CheckBoxCornerRadius), default(CornerRadius));
    public static readonly DependencyProperty SelectionIndicatorCornerRadiusProperty = Register(nameof(SelectionIndicatorCornerRadius), default(CornerRadius));
    public static readonly DependencyProperty SelectionIndicatorVisualEnabledProperty = Register(nameof(SelectionIndicatorVisualEnabled), false);
    public static readonly DependencyProperty SelectionIndicatorModeProperty = Register(nameof(SelectionIndicatorMode), ListViewItemPresenterSelectionIndicatorMode.Inline);
    public static readonly DependencyProperty SelectionIndicatorBrushProperty = Register<Brush?>(nameof(SelectionIndicatorBrush), null);
    public static readonly DependencyProperty SelectionIndicatorPointerOverBrushProperty = Register<Brush?>(nameof(SelectionIndicatorPointerOverBrush), null);
    public static readonly DependencyProperty SelectionIndicatorPressedBrushProperty = Register<Brush?>(nameof(SelectionIndicatorPressedBrush), null);
    public static readonly DependencyProperty SelectionIndicatorDisabledBrushProperty = Register<Brush?>(nameof(SelectionIndicatorDisabledBrush), null);
    public static readonly DependencyProperty SelectedBorderBrushProperty = Register<Brush?>(nameof(SelectedBorderBrush), null);
    public static readonly DependencyProperty SelectedPressedBorderBrushProperty = Register<Brush?>(nameof(SelectedPressedBorderBrush), null);
    public static readonly DependencyProperty SelectedDisabledBorderBrushProperty = Register<Brush?>(nameof(SelectedDisabledBorderBrush), null);
    public static readonly DependencyProperty SelectedInnerBorderBrushProperty = Register<Brush?>(nameof(SelectedInnerBorderBrush), null);
    public static readonly DependencyProperty PointerOverBorderBrushProperty = Register<Brush?>(nameof(PointerOverBorderBrush), null);

    public bool SelectionCheckMarkVisualEnabled { get => Get<bool>(SelectionCheckMarkVisualEnabledProperty); set => SetValue(SelectionCheckMarkVisualEnabledProperty, value); }
    public Brush? CheckHintBrush { get => GetBrush(CheckHintBrushProperty); set => SetValue(CheckHintBrushProperty, value); }
    public Brush? CheckSelectingBrush { get => GetBrush(CheckSelectingBrushProperty); set => SetValue(CheckSelectingBrushProperty, value); }
    public Brush? CheckBrush { get => GetBrush(CheckBrushProperty); set => SetValue(CheckBrushProperty, value); }
    public Brush? DragBackground { get => GetBrush(DragBackgroundProperty); set => SetValue(DragBackgroundProperty, value); }
    public Brush? DragForeground { get => GetBrush(DragForegroundProperty); set => SetValue(DragForegroundProperty, value); }
    public Brush? FocusBorderBrush { get => GetBrush(FocusBorderBrushProperty); set => SetValue(FocusBorderBrushProperty, value); }
    public Brush? PlaceholderBackground { get => GetBrush(PlaceholderBackgroundProperty); set => SetValue(PlaceholderBackgroundProperty, value); }
    public Brush? PointerOverBackground { get => GetBrush(PointerOverBackgroundProperty); set => SetValue(PointerOverBackgroundProperty, value); }
    public Brush? SelectedBackground { get => GetBrush(SelectedBackgroundProperty); set => SetValue(SelectedBackgroundProperty, value); }
    public Brush? SelectedForeground { get => GetBrush(SelectedForegroundProperty); set => SetValue(SelectedForegroundProperty, value); }
    public Brush? SelectedPointerOverBackground { get => GetBrush(SelectedPointerOverBackgroundProperty); set => SetValue(SelectedPointerOverBackgroundProperty, value); }
    public Brush? SelectedPointerOverBorderBrush { get => GetBrush(SelectedPointerOverBorderBrushProperty); set => SetValue(SelectedPointerOverBorderBrushProperty, value); }
    public Thickness SelectedBorderThickness { get => Get<Thickness>(SelectedBorderThicknessProperty); set => SetValue(SelectedBorderThicknessProperty, value); }
    public double DisabledOpacity { get => Get<double>(DisabledOpacityProperty); set => SetValue(DisabledOpacityProperty, value); }
    public double DragOpacity { get => Get<double>(DragOpacityProperty); set => SetValue(DragOpacityProperty, value); }
    public double ReorderHintOffset { get => Get<double>(ReorderHintOffsetProperty); set => SetValue(ReorderHintOffsetProperty, value); }
    public Thickness PointerOverBackgroundMargin { get => Get<Thickness>(PointerOverBackgroundMarginProperty); set => SetValue(PointerOverBackgroundMarginProperty, value); }
    public Thickness ContentMargin { get => Get<Thickness>(ContentMarginProperty); set => SetValue(ContentMarginProperty, value); }
    public Brush? SelectedPressedBackground { get => GetBrush(SelectedPressedBackgroundProperty); set => SetValue(SelectedPressedBackgroundProperty, value); }
    public Brush? PressedBackground { get => GetBrush(PressedBackgroundProperty); set => SetValue(PressedBackgroundProperty, value); }
    public Brush? CheckBoxBrush { get => GetBrush(CheckBoxBrushProperty); set => SetValue(CheckBoxBrushProperty, value); }
    public Brush? FocusSecondaryBorderBrush { get => GetBrush(FocusSecondaryBorderBrushProperty); set => SetValue(FocusSecondaryBorderBrushProperty, value); }
    public ListViewItemPresenterCheckMode CheckMode { get => Get<ListViewItemPresenterCheckMode>(CheckModeProperty); set => SetValue(CheckModeProperty, value); }
    public Brush? PointerOverForeground { get => GetBrush(PointerOverForegroundProperty); set => SetValue(PointerOverForegroundProperty, value); }
    public Brush? RevealBackground { get => GetBrush(RevealBackgroundProperty); set => SetValue(RevealBackgroundProperty, value); }
    public Brush? RevealBorderBrush { get => GetBrush(RevealBorderBrushProperty); set => SetValue(RevealBorderBrushProperty, value); }
    public Thickness RevealBorderThickness { get => Get<Thickness>(RevealBorderThicknessProperty); set => SetValue(RevealBorderThicknessProperty, value); }
    public bool RevealBackgroundShowsAboveContent { get => Get<bool>(RevealBackgroundShowsAboveContentProperty); set => SetValue(RevealBackgroundShowsAboveContentProperty, value); }
    public Brush? SelectedDisabledBackground { get => GetBrush(SelectedDisabledBackgroundProperty); set => SetValue(SelectedDisabledBackgroundProperty, value); }
    public Brush? CheckPressedBrush { get => GetBrush(CheckPressedBrushProperty); set => SetValue(CheckPressedBrushProperty, value); }
    public Brush? CheckDisabledBrush { get => GetBrush(CheckDisabledBrushProperty); set => SetValue(CheckDisabledBrushProperty, value); }
    public Brush? CheckBoxPointerOverBrush { get => GetBrush(CheckBoxPointerOverBrushProperty); set => SetValue(CheckBoxPointerOverBrushProperty, value); }
    public Brush? CheckBoxPressedBrush { get => GetBrush(CheckBoxPressedBrushProperty); set => SetValue(CheckBoxPressedBrushProperty, value); }
    public Brush? CheckBoxDisabledBrush { get => GetBrush(CheckBoxDisabledBrushProperty); set => SetValue(CheckBoxDisabledBrushProperty, value); }
    public Brush? CheckBoxSelectedBrush { get => GetBrush(CheckBoxSelectedBrushProperty); set => SetValue(CheckBoxSelectedBrushProperty, value); }
    public Brush? CheckBoxSelectedPointerOverBrush { get => GetBrush(CheckBoxSelectedPointerOverBrushProperty); set => SetValue(CheckBoxSelectedPointerOverBrushProperty, value); }
    public Brush? CheckBoxSelectedPressedBrush { get => GetBrush(CheckBoxSelectedPressedBrushProperty); set => SetValue(CheckBoxSelectedPressedBrushProperty, value); }
    public Brush? CheckBoxSelectedDisabledBrush { get => GetBrush(CheckBoxSelectedDisabledBrushProperty); set => SetValue(CheckBoxSelectedDisabledBrushProperty, value); }
    public Brush? CheckBoxBorderBrush { get => GetBrush(CheckBoxBorderBrushProperty); set => SetValue(CheckBoxBorderBrushProperty, value); }
    public Brush? CheckBoxPointerOverBorderBrush { get => GetBrush(CheckBoxPointerOverBorderBrushProperty); set => SetValue(CheckBoxPointerOverBorderBrushProperty, value); }
    public Brush? CheckBoxPressedBorderBrush { get => GetBrush(CheckBoxPressedBorderBrushProperty); set => SetValue(CheckBoxPressedBorderBrushProperty, value); }
    public Brush? CheckBoxDisabledBorderBrush { get => GetBrush(CheckBoxDisabledBorderBrushProperty); set => SetValue(CheckBoxDisabledBorderBrushProperty, value); }
    public CornerRadius CheckBoxCornerRadius { get => Get<CornerRadius>(CheckBoxCornerRadiusProperty); set => SetValue(CheckBoxCornerRadiusProperty, value); }
    public CornerRadius SelectionIndicatorCornerRadius { get => Get<CornerRadius>(SelectionIndicatorCornerRadiusProperty); set => SetValue(SelectionIndicatorCornerRadiusProperty, value); }
    public bool SelectionIndicatorVisualEnabled { get => Get<bool>(SelectionIndicatorVisualEnabledProperty); set => SetValue(SelectionIndicatorVisualEnabledProperty, value); }
    public ListViewItemPresenterSelectionIndicatorMode SelectionIndicatorMode { get => Get<ListViewItemPresenterSelectionIndicatorMode>(SelectionIndicatorModeProperty); set => SetValue(SelectionIndicatorModeProperty, value); }
    public Brush? SelectionIndicatorBrush { get => GetBrush(SelectionIndicatorBrushProperty); set => SetValue(SelectionIndicatorBrushProperty, value); }
    public Brush? SelectionIndicatorPointerOverBrush { get => GetBrush(SelectionIndicatorPointerOverBrushProperty); set => SetValue(SelectionIndicatorPointerOverBrushProperty, value); }
    public Brush? SelectionIndicatorPressedBrush { get => GetBrush(SelectionIndicatorPressedBrushProperty); set => SetValue(SelectionIndicatorPressedBrushProperty, value); }
    public Brush? SelectionIndicatorDisabledBrush { get => GetBrush(SelectionIndicatorDisabledBrushProperty); set => SetValue(SelectionIndicatorDisabledBrushProperty, value); }
    public Brush? SelectedBorderBrush { get => GetBrush(SelectedBorderBrushProperty); set => SetValue(SelectedBorderBrushProperty, value); }
    public Brush? SelectedPressedBorderBrush { get => GetBrush(SelectedPressedBorderBrushProperty); set => SetValue(SelectedPressedBorderBrushProperty, value); }
    public Brush? SelectedDisabledBorderBrush { get => GetBrush(SelectedDisabledBorderBrushProperty); set => SetValue(SelectedDisabledBorderBrushProperty, value); }
    public Brush? SelectedInnerBorderBrush { get => GetBrush(SelectedInnerBorderBrushProperty); set => SetValue(SelectedInnerBorderBrushProperty, value); }
    public Brush? PointerOverBorderBrush { get => GetBrush(PointerOverBorderBrushProperty); set => SetValue(PointerOverBorderBrushProperty, value); }

    public override void OnRender(DrawingContext context)
    {
        var item = FindSelectorItem();
        var background = ResolveBackground(item);
        var border = ResolveBorder(item);
        var thickness = item?.IsSelected == true ? SelectedBorderThickness : RevealBorderThickness;
        if (background != null || (border != null && thickness.Left > 0))
        {
            var pen = border != null && thickness.Left > 0 ? new Pen(border, thickness.Left) : null;
            context.DrawRoundedRectangle(background, pen, new Rect(Vector2.Zero, Size), CornerRadius.RenderingRadius);
        }
        base.OnRender(context);
    }

    private SelectorItem? FindSelectorItem()
    {
        DependencyObject? current = Parent as DependencyObject;
        while (current != null)
        {
            if (current is SelectorItem item) return item;
            current = current.Parent as DependencyObject;
        }
        return null;
    }

    private Brush? ResolveBackground(SelectorItem? item)
    {
        if (item == null) return RevealBackground ?? Background;
        if (!item.IsEnabled && item.IsSelected) return SelectedDisabledBackground ?? SelectedBackground;
        if (item.IsSelected && item.IsPointerPressed) return SelectedPressedBackground ?? SelectedBackground;
        if (item.IsSelected && item.IsPointerOver) return SelectedPointerOverBackground ?? SelectedBackground;
        if (item.IsSelected) return SelectedBackground;
        if (item.IsPointerPressed) return PressedBackground;
        if (item.IsPointerOver) return PointerOverBackground;
        return RevealBackground ?? Background;
    }

    private Brush? ResolveBorder(SelectorItem? item)
    {
        if (item == null) return RevealBorderBrush ?? BorderBrush;
        if (!item.IsEnabled && item.IsSelected) return SelectedDisabledBorderBrush ?? SelectedBorderBrush;
        if (item.IsSelected && item.IsPointerPressed) return SelectedPressedBorderBrush ?? SelectedBorderBrush;
        if (item.IsSelected && item.IsPointerOver) return SelectedPointerOverBorderBrush ?? SelectedBorderBrush;
        if (item.IsSelected) return SelectedBorderBrush;
        if (item.IsPointerOver) return PointerOverBorderBrush;
        return RevealBorderBrush ?? BorderBrush;
    }

    private Brush? GetBrush(DependencyProperty property) => GetValue(property) as Brush;
    private T Get<T>(DependencyProperty property) => (T)GetValue(property)!;
    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(ListViewItemPresenter),
            new PropertyMetadata(defaultValue) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });
}
