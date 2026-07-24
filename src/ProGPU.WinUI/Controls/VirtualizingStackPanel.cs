using System;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Layout;
using ProGPU.Scene;
using Microsoft.UI.Xaml;
using ProGPU.Virtualization;

namespace Microsoft.UI.Xaml.Controls;

public class VirtualizingStackPanel : VirtualizingPanel
{
    public static readonly DependencyProperty AreScrollSnapPointsRegularProperty =
        DependencyProperty.Register(
            nameof(AreScrollSnapPointsRegular), typeof(bool), typeof(VirtualizingStackPanel),
            new PropertyMetadata(false));

    private int _itemsCount = 0;
    private float _itemWidth = 300f;
    private float _itemHeight = 40f;
    private Orientation _orientation = Orientation.Vertical;
    private float _estimatedItemHeight = 40f;
    private float _cacheLength = 1f;
    private readonly VariableSizeIndex _verticalSizeIndex = new();
    private readonly List<int> _indicesToRecycle = new();
    private int _indexedItemCount = -1;
    private float _indexedEstimate = -1f;
    private float _indexedWidth = -1f;
    private bool _isUpdatingViewport;

    public bool AreScrollSnapPointsRegular
    {
        get => (bool)(GetValue(AreScrollSnapPointsRegularProperty) ?? false);
        set => SetValue(AreScrollSnapPointsRegularProperty, value);
    }

    public VirtualizingStackPanel()
    {
        ThemeManager.ThemeChanged += OnThemeManagerChanged;
    }

    private void OnThemeManagerChanged()
    {
        _recycledVisuals.Clear();
        Invalidate();
    }

    private Func<Visual>? _createVisualFactory;
    private Action<Visual, int>? _bindVisualCallback;

    public Func<Visual>? CreateVisualFactory
    {
        get => ItemsControlOwner != null ? ItemsControlOwner.ItemVisualFactory : _createVisualFactory;
        set => _createVisualFactory = value;
    }

    public Action<Visual, int>? BindVisualCallback
    {
        get
        {
            if (ItemsControlOwner != null)
            {
                return (visual, index) =>
                {
                    var item = GetItemAt(index);
                    if (item != null)
                    {
                        ItemsControlOwner.BindVisualCallback?.Invoke(visual, item, index);
                    }
                };
            }
            return _bindVisualCallback;
        }
        set => _bindVisualCallback = value;
    }

    private readonly Stack<Visual> _recycledVisuals = new();
    private readonly Dictionary<int, Visual> _activeVisuals = new();

    public int ItemsCount
    {
        get => ItemsControlOwner != null ? GetItemsCount() : _itemsCount;
        set
        {
            if (ItemsControlOwner == null)
            {
                if (_itemsCount != value)
                {
                    _itemsCount = value;
                    UpdateViewport();
                    Invalidate();
                }
            }
        }
    }

    public float ItemWidth
    {
        get => _itemWidth;
        set
        {
            if (_itemWidth != value)
            {
                _itemWidth = value;
                UpdateViewport();
                Invalidate();
            }
        }
    }

    public float ItemHeight
    {
        get => _itemHeight;
        set
        {
            if (_itemHeight != value)
            {
                _itemHeight = value;
                UpdateViewport();
                Invalidate();
            }
        }
    }

    /// <summary>
    /// Estimated height used until an auto-sized item is realized and measured.
    /// Set <see cref="ItemHeight"/> to <see cref="float.NaN"/> to enable variable sizes.
    /// </summary>
    public float EstimatedItemHeight
    {
        get => _estimatedItemHeight;
        set
        {
            if (!float.IsFinite(value) || value <= 0f) throw new ArgumentOutOfRangeException(nameof(value));
            if (_estimatedItemHeight == value) return;
            _estimatedItemHeight = value;
            ResetSizeIndex();
            UpdateViewport();
            Invalidate();
        }
    }

    /// <summary>Realization cache measured in viewport lengths on each scroll side.</summary>
    public float CacheLength
    {
        get => _cacheLength;
        set
        {
            if (!float.IsFinite(value) || value < 0f) throw new ArgumentOutOfRangeException(nameof(value));
            if (_cacheLength == value) return;
            _cacheLength = value;
            UpdateViewport();
        }
    }

    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation != value)
            {
                _orientation = value;
                UpdateViewport();
                Invalidate();
            }
        }
    }

    public override float TotalVirtualHeight => Orientation == Orientation.Vertical
        ? IsVariableHeight ? EnsureSizeIndex().TotalSize : ItemsCount * ItemHeight
        : ViewportHeight;
    public override float TotalVirtualWidth => Orientation == Orientation.Horizontal ? ItemsCount * ItemWidth : ViewportWidth;
    public override bool IsHorizontal => Orientation == Orientation.Horizontal;

    protected override void OnScrollOffsetChanged(float newOffset)
    {
        UpdateViewport();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        float width = Orientation == Orientation.Horizontal 
            ? (float.IsInfinity(availableSize.X) ? TotalVirtualWidth : availableSize.X)
            : (float.IsInfinity(availableSize.X) ? 400f : availableSize.X);
            
        float height = Orientation == Orientation.Vertical 
            ? (float.IsInfinity(availableSize.Y) ? TotalVirtualHeight : availableSize.Y)
            : (float.IsInfinity(availableSize.Y) ? 400f : availableSize.Y);

        UpdateViewport();

        return new Vector2(width, height);
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        base.ArrangeOverride(arrangeRect);
        UpdateViewport();
    }

    private void UpdateViewport()
    {
        if (_isUpdatingViewport) return;
        _isUpdatingViewport = true;
        try
        {
        int itemsCount = ItemsCount;
        var createVisual = CreateVisualFactory;
        var bindVisual = BindVisualCallback;

        float viewportWidth = ViewportWidth;
        float viewportHeight = ViewportHeight;

        if (itemsCount == 0 || createVisual == null || bindVisual == null || viewportHeight <= 0 || viewportWidth <= 0)
        {
            ClearActiveToRecycler();
            return;
        }

        if (Orientation == Orientation.Vertical)
        {
            VariableSizeIndex? sizeIndex = IsVariableHeight ? EnsureSizeIndex(viewportWidth) : null;
            float realizationPadding = viewportHeight * CacheLength;
            float realizationStart = Math.Max(0f, ScrollOffset - realizationPadding);
            float realizationEnd = ScrollOffset + viewportHeight + realizationPadding;
            int startIdx = sizeIndex?.GetIndexAtOffset(realizationStart) ??
                (int)Math.Floor(realizationStart / ItemHeight);
            int endIdx = sizeIndex?.GetIndexAtOffset(realizationEnd) ??
                (int)Math.Ceiling(realizationEnd / ItemHeight);
            int anchorIndex = sizeIndex?.GetIndexAtOffset(ScrollOffset) ?? startIdx;

            startIdx = Math.Clamp(startIdx, 0, itemsCount - 1);
            endIdx = Math.Clamp(endIdx, 0, itemsCount - 1);

            // 2. Recycle items scrolled out of view
            _indicesToRecycle.Clear();
            foreach (var key in _activeVisuals.Keys)
            {
                if (key < startIdx || key > endIdx)
                {
                    _indicesToRecycle.Add(key);
                }
            }

            foreach (var idx in _indicesToRecycle)
            {
                var vis = _activeVisuals[idx];
                _activeVisuals.Remove(idx);
                RemoveChild(vis);
                _recycledVisuals.Push(vis);
            }

            // 3. Position and Bind newly visible items
            for (int i = startIdx; i <= endIdx; i++)
            {
                if (!_activeVisuals.TryGetValue(i, out var visual))
                {
                    visual = _recycledVisuals.Count > 0 ? _recycledVisuals.Pop() : createVisual();
                    bindVisual(visual, i);
                    _activeVisuals[i] = visual;
                    AddChild(visual);
                }

                float itemHeight = sizeIndex?.GetSize(i) ?? ItemHeight;
                if (sizeIndex != null && !sizeIndex.IsMeasured(i) && visual is LayoutNode measuredNode)
                {
                    measuredNode.Measure(new Vector2(viewportWidth, float.PositiveInfinity));
                    itemHeight = Math.Max(1f, measuredNode.DesiredSize.Y);
                    float delta = sizeIndex.SetMeasuredSize(i, itemHeight);
                    if (delta != 0f && i < anchorIndex)
                    {
                        ScrollOffset += delta;
                    }
                }

                float posY = sizeIndex?.GetOffset(i) ?? i * ItemHeight;
                if (ScrollViewerOwner == null)
                {
                    posY = MathF.Round(posY - ScrollOffset);
                }
                float itemWidth = viewportWidth;

                visual.Offset = new Vector2(0f, posY);
                visual.Size = new Vector2(itemWidth, itemHeight);

                if (visual is LayoutNode childNode)
                {
                    if (sizeIndex == null) childNode.Measure(new Vector2(itemWidth, itemHeight));
                    childNode.Arrange(new Rect(0f, posY, itemWidth, itemHeight));
                }

                if (sizeIndex != null && i == endIdx && i + 1 < itemsCount &&
                    sizeIndex.GetOffset(i + 1) < realizationEnd)
                {
                    endIdx++;
                }
            }
        }
        else
        {
            // Horizontal scrolling (ScrollOffset is horizontal)
            int startIdx = (int)Math.Floor(ScrollOffset / ItemWidth);
            int endIdx = (int)Math.Ceiling((ScrollOffset + viewportWidth) / ItemWidth);

            startIdx = Math.Clamp(startIdx, 0, itemsCount - 1);
            endIdx = Math.Clamp(endIdx, 0, itemsCount - 1);

            _indicesToRecycle.Clear();
            foreach (var key in _activeVisuals.Keys)
            {
                if (key < startIdx || key > endIdx)
                {
                    _indicesToRecycle.Add(key);
                }
            }

            foreach (var idx in _indicesToRecycle)
            {
                var vis = _activeVisuals[idx];
                _activeVisuals.Remove(idx);
                RemoveChild(vis);
                _recycledVisuals.Push(vis);
            }

            for (int i = startIdx; i <= endIdx; i++)
            {
                if (!_activeVisuals.TryGetValue(i, out var visual))
                {
                    visual = _recycledVisuals.Count > 0 ? _recycledVisuals.Pop() : createVisual();
                    bindVisual(visual, i);
                    _activeVisuals[i] = visual;
                    AddChild(visual);
                }

                float posX = i * ItemWidth;
                if (ScrollViewerOwner == null)
                {
                    posX = MathF.Round(posX - ScrollOffset);
                }
                float itemHeight = viewportHeight;

                visual.Offset = new Vector2(posX, 0f);
                visual.Size = new Vector2(ItemWidth, itemHeight);

                if (visual is LayoutNode childNode)
                {
                    childNode.Measure(new Vector2(ItemWidth, itemHeight));
                    childNode.Arrange(new Rect(posX, 0f, ItemWidth, itemHeight));
                }
            }
        }
        }
        finally
        {
            _isUpdatingViewport = false;
        }
    }

    private bool IsVariableHeight => float.IsNaN(ItemHeight);

    private VariableSizeIndex EnsureSizeIndex(float viewportWidth = float.NaN)
    {
        int count = ItemsCount;
        if (_indexedEstimate != EstimatedItemHeight || _indexedItemCount < 0)
        {
            _verticalSizeIndex.Reset(count, EstimatedItemHeight);
            _indexedEstimate = EstimatedItemHeight;
        }
        else if (_indexedItemCount < count)
        {
            _verticalSizeIndex.InsertRange(_indexedItemCount, count - _indexedItemCount);
        }
        else if (_indexedItemCount > count)
        {
            _verticalSizeIndex.RemoveRange(count, _indexedItemCount - count);
        }
        if (float.IsFinite(viewportWidth) && viewportWidth > 0f && _indexedWidth != viewportWidth)
        {
            _verticalSizeIndex.InvalidateAllMeasurements();
            _indexedWidth = viewportWidth;
        }
        _indexedItemCount = count;
        return _verticalSizeIndex;
    }

    private void ResetSizeIndex()
    {
        _indexedItemCount = -1;
        _indexedEstimate = -1f;
        _indexedWidth = -1f;
    }

    private void ClearActiveToRecycler()
    {
        foreach (var vis in _activeVisuals.Values)
        {
            RemoveChild(vis);
            _recycledVisuals.Push(vis);
        }
        _activeVisuals.Clear();
    }

    public override void ForceRebind()
    {
        ClearActiveToRecycler();
        ResetSizeIndex();
        base.ForceRebind();
    }

    public override void RebindVisibleItems()
    {
        var bindVisual = BindVisualCallback;
        if (bindVisual == null) return;
        foreach (var pair in _activeVisuals) bindVisual(pair.Value, pair.Key);
        if (IsVariableHeight) _verticalSizeIndex.InvalidateAllMeasurements();
        UpdateViewport();
        Invalidate();
    }
}
