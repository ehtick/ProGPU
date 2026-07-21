using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace ProGPU.Android;

internal sealed class AndroidTextInputBridge : IDisposable
{
    private readonly Activity _activity;
    private readonly BridgeEditText _editText;
    private WindowInputState? _inputState;
    private bool _acceptsReturn;
    private bool _synchronizing;
    private bool _compositionActive;
    private string _lastText = string.Empty;
    private string _compositionBaselineText = string.Empty;
    private int _compositionStart;
    private int _compositionOriginalLength;
    private bool _disposed;

    public AndroidTextInputBridge(Activity activity)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _editText = new BridgeEditText(activity)
        {
            Background = null,
            Alpha = 0.01f,
            ImportantForAccessibility = ImportantForAccessibility.NoHideDescendants
        };
        _editText.SetCursorVisible(false);
        _editText.SetIncludeFontPadding(false);
        _editText.SetSingleLine(true);
        _editText.SetPadding(0, 0, 0, 0);
        _editText.SetTextColor(Color.Transparent);
        _editText.SetHintTextColor(Color.Transparent);
        _editText.SelectionChanged += OnNativeSelectionChanged;
        _editText.AfterTextChanged += OnAfterTextChanged;
        _editText.EditorAction += OnEditorAction;
    }

    public View NativeView => _editText;

    public bool IsActive => _editText.HasFocus && _inputState != null;

    public void Attach(WindowInputState inputState)
    {
        ArgumentNullException.ThrowIfNull(inputState);
        if (_inputState != null && !ReferenceEquals(_inputState, inputState)) Detach(_inputState);
        _inputState = inputState;
        inputState.FocusChanged = OnFocusChanged;
    }

    public void Detach(WindowInputState inputState)
    {
        if (inputState.FocusChanged == OnFocusChanged) inputState.FocusChanged = null;
        if (ReferenceEquals(_inputState, inputState)) _inputState = null;
        TryHide();
    }

    public bool TryShow()
    {
        if (_inputState == null || InputSystem.FocusedElement is not ITextInputClient) return false;
        _editText.RequestFocus();
        var manager = (InputMethodManager?)_activity.GetSystemService(Context.InputMethodService);
        return manager?.ShowSoftInput(_editText, ShowFlags.Implicit) == true;
    }

    public bool TryHide()
    {
        var manager = (InputMethodManager?)_activity.GetSystemService(Context.InputMethodService);
        bool hidden = manager?.HideSoftInputFromWindow(_editText.WindowToken, HideSoftInputFlags.None) == true;
        _editText.ClearFocus();
        return hidden;
    }

    private WindowInputState FindInputState() =>
        _inputState ?? throw new InvalidOperationException("The Android text bridge is not attached.");

    private void OnFocusChanged(FrameworkElement? focusedElement)
    {
        if (focusedElement is not ITextInputClient client)
        {
            TryHide();
            return;
        }

        InputSystem.Current = FindInputState();
        TextInputOptions options = client.GetTextInputOptions();
        _acceptsReturn = options.AcceptsReturn;
        _editText.InputType = MapInputType(options);
        _editText.ImeOptions = MapImeAction(options.EnterKeyHint, options.AcceptsReturn);
        _editText.SetSingleLine(!options.AcceptsReturn);
        PositionNativeEditor(options);
        SynchronizeNativeDocument(options);
        _editText.RequestFocus();
        _editText.Post(() => TryShow());
    }

    private void PositionNativeEditor(TextInputOptions options)
    {
        float density = ResolveDensity();
        if (_editText.LayoutParameters is not FrameLayout.LayoutParams layout) return;
        layout.Width = Math.Max(1, checked((int)MathF.Ceiling(options.Bounds.Width * density)));
        layout.Height = Math.Max(1, checked((int)MathF.Ceiling(options.Bounds.Height * density)));
        layout.LeftMargin = checked((int)MathF.Round(options.Bounds.X * density));
        layout.TopMargin = checked((int)MathF.Round(options.Bounds.Y * density));
        _editText.LayoutParameters = layout;
    }

    private void SynchronizeNativeDocument(TextInputOptions options)
    {
        _synchronizing = true;
        try
        {
            _compositionActive = false;
            _lastText = options.Text ?? string.Empty;
            _editText.SetText(_lastText, TextView.BufferType.Editable);
            int start = Math.Clamp(options.SelectionStart, 0, _lastText.Length);
            int end = Math.Clamp(start + options.SelectionLength, start, _lastText.Length);
            _editText.SetSelection(start, end);
        }
        finally
        {
            _synchronizing = false;
        }
    }

    private void OnAfterTextChanged(object? sender, AfterTextChangedEventArgs args)
    {
        if (_synchronizing || _inputState == null) return;
        InputSystem.Current = FindInputState();
        string current = _editText.Text ?? string.Empty;
        IEditable? editable = _editText.EditableText;
        int markedStart = editable == null ? -1 : BaseInputConnection.GetComposingSpanStart(editable);
        int markedEnd = editable == null ? -1 : BaseInputConnection.GetComposingSpanEnd(editable);
        if (markedStart >= 0 && markedEnd >= markedStart)
        {
            string markedText = current.Substring(
                Math.Clamp(markedStart, 0, current.Length),
                Math.Clamp(markedEnd - markedStart, 0, current.Length - Math.Clamp(markedStart, 0, current.Length)));
            if (!_compositionActive)
            {
                _compositionActive = true;
                _compositionBaselineText = _lastText;
                _compositionStart = Math.Clamp(markedStart, 0, _compositionBaselineText.Length);
                _compositionOriginalLength = Math.Clamp(
                    _compositionBaselineText.Length + markedText.Length - current.Length,
                    0,
                    _compositionBaselineText.Length - _compositionStart);
                InputSystem.InjectTextInput(TextInputEventKind.CompositionStarted, isComposing: true);
            }
            InputSystem.InjectTextInput(TextInputEventKind.CompositionUpdated, markedText, isComposing: true);
            _lastText = current;
            return;
        }

        if (_compositionActive)
        {
            if (string.Equals(current, _compositionBaselineText, StringComparison.Ordinal))
            {
                InputSystem.InjectTextInput(TextInputEventKind.CompositionCanceled);
            }
            else
            {
                int finalLength = Math.Max(
                    0,
                    current.Length - (_compositionBaselineText.Length - _compositionOriginalLength));
                finalLength = Math.Min(finalLength, Math.Max(0, current.Length - _compositionStart));
                string committed = current.Substring(_compositionStart, finalLength);
                InputSystem.InjectTextInput(TextInputEventKind.CompositionCompleted, committed);
            }
            _compositionActive = false;
            _lastText = current;
            return;
        }

        FindSingleReplacement(_lastText, current, out int start, out int removedLength, out string inserted);
        int selectionStart = Math.Max(0, _editText.SelectionStart);
        int selectionEnd = Math.Max(selectionStart, _editText.SelectionEnd);
        InputSystem.InjectTextReplacement(
            inserted,
            start,
            removedLength,
            selectionStart,
            selectionEnd - selectionStart);
        _lastText = current;
    }

    private void OnNativeSelectionChanged(int start, int end)
    {
        if (_synchronizing || _compositionActive || _inputState == null) return;
        InputSystem.Current = FindInputState();
        start = Math.Max(0, start);
        end = Math.Max(start, end);
        InputSystem.InjectTextSelection(start, end - start);
    }

    private void OnEditorAction(object? sender, TextView.EditorActionEventArgs args)
    {
        if (_inputState == null) return;
        InputSystem.Current = FindInputState();
        InputSystem.InjectTextInput(TextInputEventKind.InsertLineBreak, "\n");
        if (!_acceptsReturn) TryHide();
        args.Handled = true;
    }

    private static void FindSingleReplacement(
        string oldText,
        string newText,
        out int start,
        out int removedLength,
        out string inserted)
    {
        int prefix = 0;
        int commonLimit = Math.Min(oldText.Length, newText.Length);
        while (prefix < commonLimit && oldText[prefix] == newText[prefix]) prefix++;

        int suffix = 0;
        while (suffix < oldText.Length - prefix &&
               suffix < newText.Length - prefix &&
               oldText[oldText.Length - 1 - suffix] == newText[newText.Length - 1 - suffix])
        {
            suffix++;
        }

        start = prefix;
        removedLength = oldText.Length - prefix - suffix;
        inserted = newText.Substring(prefix, newText.Length - prefix - suffix);
    }

    private static InputTypes MapInputType(TextInputOptions options)
    {
        InputTypes type = options.InputScope switch
        {
            InputScopeNameValue.Number => InputTypes.ClassNumber | InputTypes.NumberFlagDecimal | InputTypes.NumberFlagSigned,
            InputScopeNameValue.NumericPin => InputTypes.ClassNumber | InputTypes.NumberVariationPassword,
            InputScopeNameValue.TelephoneNumber => InputTypes.ClassPhone,
            InputScopeNameValue.Url => InputTypes.ClassText | InputTypes.TextVariationUri,
            InputScopeNameValue.EmailSmtpAddress => InputTypes.ClassText | InputTypes.TextVariationEmailAddress,
            InputScopeNameValue.Password => InputTypes.ClassText | InputTypes.TextVariationPassword,
            _ => InputTypes.ClassText | InputTypes.TextVariationNormal
        };
        if (options.IsPassword) type = InputTypes.ClassText | InputTypes.TextVariationPassword;
        if (options.AcceptsReturn) type |= InputTypes.TextFlagMultiLine | InputTypes.TextFlagCapSentences;
        if (!options.IsSpellCheckEnabled) type |= InputTypes.TextFlagNoSuggestions;
        type |= options.AutoCapitalize.ToLowerInvariant() switch
        {
            "allcharacters" or "characters" => InputTypes.TextFlagCapCharacters,
            "words" => InputTypes.TextFlagCapWords,
            "sentences" => InputTypes.TextFlagCapSentences,
            _ => 0
        };
        return type;
    }

    private static ImeAction MapImeAction(string hint, bool acceptsReturn)
    {
        if (acceptsReturn) return ImeAction.None;
        return hint.ToLowerInvariant() switch
        {
            "done" => ImeAction.Done,
            "go" => ImeAction.Go,
            "next" => ImeAction.Next,
            "search" => ImeAction.Search,
            "send" => ImeAction.Send,
            _ => ImeAction.Done
        };
    }

    private float ResolveDensity()
    {
        float density = _activity.Resources?.DisplayMetrics?.Density ?? 1f;
        return float.IsFinite(density) && density > 0f ? density : 1f;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_inputState != null) Detach(_inputState);
        _editText.SelectionChanged -= OnNativeSelectionChanged;
        _editText.AfterTextChanged -= OnAfterTextChanged;
        _editText.EditorAction -= OnEditorAction;
        _editText.Dispose();
        _disposed = true;
    }

    private sealed class BridgeEditText(Context context) : EditText(context)
    {
        public event Action<int, int>? SelectionChanged;

        // This view exists only to supply Android's InputConnection. ProGPU owns hit
        // testing and selection, so pointer streams must continue to the SurfaceView.
        public override bool OnTouchEvent(MotionEvent? e) => false;

        public override bool OnGenericMotionEvent(MotionEvent? e) => false;

        public override bool OnHoverEvent(MotionEvent? e) => false;

        protected override void OnSelectionChanged(int selStart, int selEnd)
        {
            base.OnSelectionChanged(selStart, selEnd);
            SelectionChanged?.Invoke(selStart, selEnd);
        }
    }
}
