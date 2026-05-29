using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Numerics;
using System.Threading.Tasks;
using ProGPU.Layout;
using ProGPU.Scene;
using ProGPU.Vector;

namespace Microsoft.UI.Xaml.Controls;

public enum ContentDialogResult
{
    None,
    Primary,
    Secondary,
    Close
}

public class ContentDialog : Control
{
    private string _title = "Dialog Title";
    private object? _content;
    private string _primaryButtonText = string.Empty;
    private string _secondaryButtonText = string.Empty;
    private string _closeButtonText = "Close";

    private TaskCompletionSource<ContentDialogResult>? _tcs;

    private Button? _btnPrimary;
    private Button? _btnSecondary;
    private Button? _btnClose;
    private Border _cardBorder;
    private StackPanel _cardStack;

    public string Title
    {
        get => _title;
        set { _title = value; Invalidate(); }
    }

    public object? Content
    {
        get => _content;
        set { _content = value; Invalidate(); }
    }

    public string PrimaryButtonText
    {
        get => _primaryButtonText;
        set { _primaryButtonText = value; Invalidate(); }
    }

    public string SecondaryButtonText
    {
        get => _secondaryButtonText;
        set { _secondaryButtonText = value; Invalidate(); }
    }

    public string CloseButtonText
    {
        get => _closeButtonText;
        set { _closeButtonText = value; Invalidate(); }
    }

    public ContentDialog()
    {
        // Full screen hit-blocking background
        Background = new SolidColorBrush(new Vector4(0f, 0f, 0f, 0.4f)); // Semi-transparent black blocker
        
        // Build the visual card inner tree
        _cardStack = new StackPanel { Orientation = Orientation.Vertical };
        
        _cardBorder = new Border
        {
            Background = new ThemeResourceBrush("CardBackground"),
            BorderBrush = new ThemeResourceBrush("ControlBorder"),
            BorderThickness = new Thickness(1.2f),
            CornerRadius = 8f,
            Padding = new Thickness(24f),
            Child = _cardStack
        };
        
        AddChild(_cardBorder);

        var defaultStyle = ThemeManager.GetDefaultStyle(GetType());
        if (defaultStyle != null)
        {
            Style = defaultStyle;
        }
    }

    public Task<ContentDialogResult> ShowAsync()
    {
        _tcs = new TaskCompletionSource<ContentDialogResult>();

        // Build controls inside the card stack
        _cardStack.ClearChildren();

        // Title Text Block
        var titleBlock = new RichTextBlock
        {
            Font = PopupService.DefaultFont,
            FontSize = 20f,
            Foreground = new ThemeResourceBrush("TextPrimary"),
            Margin = new Thickness(0f, 0f, 0f, 16f)
        };
        titleBlock.Inlines.Add(new Run { Text = Title });
        _cardStack.AddChild(titleBlock);

        // Content
        if (Content != null)
        {
            if (Content is FrameworkElement fe)
            {
                fe.Margin = new Thickness(0f, 0f, 0f, 24f);
                _cardStack.AddChild(fe);
            }
            else
            {
                var contentBlock = new RichTextBlock
                {
                    Font = PopupService.DefaultFont,
                    FontSize = 14f,
                    Foreground = new ThemeResourceBrush("TextSecondary"),
                    Margin = new Thickness(0f, 0f, 0f, 24f)
                };
                contentBlock.Inlines.Add(new Run { Text = Content.ToString() ?? string.Empty });
                _cardStack.AddChild(contentBlock);
            }
        }

        // Horizontal Buttons Panel
        var btnStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0f)
        };

        if (!string.IsNullOrEmpty(PrimaryButtonText))
        {
            var btnText = new RichTextBlock { Font = PopupService.DefaultFont, FontSize = 14f, Foreground = new SolidColorBrush(new Vector4(1f, 1f, 1f, 1f)) };
            btnText.Inlines.Add(new Run { Text = PrimaryButtonText });

            _btnPrimary = new Button
            {
                Content = btnText,
                Width = 100f,
                Height = 32f,
                Background = new ThemeResourceBrush("SystemAccentColor"),
                Foreground = new SolidColorBrush(new Vector4(1f, 1f, 1f, 1f)),
                Margin = new Thickness(0f, 0f, 8f, 0f)
            };
            _btnPrimary.Click += (s, e) => CloseWithResult(ContentDialogResult.Primary);
            btnStack.AddChild(_btnPrimary);
        }

        if (!string.IsNullOrEmpty(SecondaryButtonText))
        {
            var btnText = new RichTextBlock { Font = PopupService.DefaultFont, FontSize = 14f, Foreground = new ThemeResourceBrush("TextPrimary") };
            btnText.Inlines.Add(new Run { Text = SecondaryButtonText });

            _btnSecondary = new Button
            {
                Content = btnText,
                Width = 100f,
                Height = 32f,
                Background = new ThemeResourceBrush("ControlBackground"),
                Foreground = new ThemeResourceBrush("TextPrimary"),
                BorderBrush = new ThemeResourceBrush("ControlBorder"),
                Margin = new Thickness(0f, 0f, 8f, 0f)
            };
            _btnSecondary.Click += (s, e) => CloseWithResult(ContentDialogResult.Secondary);
            btnStack.AddChild(_btnSecondary);
        }

        if (!string.IsNullOrEmpty(CloseButtonText))
        {
            var btnText = new RichTextBlock { Font = PopupService.DefaultFont, FontSize = 14f, Foreground = new ThemeResourceBrush("TextPrimary") };
            btnText.Inlines.Add(new Run { Text = CloseButtonText });

            _btnClose = new Button
            {
                Content = btnText,
                Width = 100f,
                Height = 32f,
                Background = new ThemeResourceBrush("ControlBackground"),
                Foreground = new ThemeResourceBrush("TextPrimary"),
                BorderBrush = new ThemeResourceBrush("ControlBorder")
            };
            _btnClose.Click += (s, e) => CloseWithResult(ContentDialogResult.Close);
            btnStack.AddChild(_btnClose);
        }

        _cardStack.AddChild(btnStack);

        // Position full-screen blocking overlay dynamically over root layout bounds
        var rootSize = InputSystem.Root?.Size ?? new Vector2(1280f, 800f);
        PopupService.ShowPopup(this, Vector2.Zero);
        
        return _tcs.Task;
    }

    private void CloseWithResult(ContentDialogResult result)
    {
        PopupService.HidePopup(this);
        _tcs?.TrySetResult(result);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var rootSize = InputSystem.Root?.Size ?? new Vector2(1280f, 800f);
        Width = rootSize.X;
        Height = rootSize.Y;

        // Card maximum width
        _cardBorder.Measure(new Vector2(Math.Min(availableSize.X - 48f, 480f), availableSize.Y - 48f));
        return rootSize;
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        var finalSize = arrangeRect.Size;
        // Center the card dialog within full-screen bounds
        float cardX = (finalSize.X - _cardBorder.DesiredSize.X) / 2f;
        float cardY = (finalSize.Y - _cardBorder.DesiredSize.Y) / 2f;

        _cardBorder.Arrange(new Rect(new Vector2(cardX, cardY), _cardBorder.DesiredSize));
    }

    public override void OnRender(DrawingContext context)
    {
        // Blurs/darkens screen context behind dialog overlay
        context.DrawRectangle(Background, null, new Rect(Vector2.Zero, Size));
        base.OnRender(context);
    }
}
