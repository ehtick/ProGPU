using System;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Layout;
using ProGPU.Scene;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Xaml.Controls;

public class VirtualizingStackPanel : VirtualizingPanel
{
    private int _itemsCount = 0;
    private float _itemWidth = 300f;
    private float _itemHeight = 40f;
    private Orientation _orientation = Orientation.Vertical;
    private readonly SceneTransformHandle _scrollTranslation = new();
    private readonly Action<Visual, int> _ownerBindVisualCallback;

    protected override bool UsesRetainedChildTranslation => ScrollViewerOwner == null;

    public VirtualizingStackPanel()
    {
        _ownerBindVisualCallback = BindOwnerVisual;
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
        get => ItemsControlOwner != null ? ItemsControlOwner.ItemTemplate : _createVisualFactory;
        set => _createVisualFactory = value;
    }

    public Action<Visual, int>? BindVisualCallback
    {
        get => ItemsControlOwner != null ? _ownerBindVisualCallback : _bindVisualCallback;
        set => _bindVisualCallback = value;
    }

    private void BindOwnerVisual(Visual visual, int index)
    {
        var itemsControl = ItemsControlOwner;
        var item = itemsControl?.GetItemAt(index);
        if (item != null)
        {
            itemsControl!.BindVisualCallback?.Invoke(visual, item, index);
        }
    }

    private readonly Stack<Visual> _recycledVisuals = new();
    private readonly Dictionary<int, Visual> _activeVisuals = new();
    private readonly List<int> _indicesToRecycle = new();
    private int _realizedStartIndex = -1;
    private int _realizedEndIndex = -1;
    private Orientation _realizedOrientation;
    private float _realizedItemWidth = float.NaN;
    private float _realizedItemHeight = float.NaN;
    private float _realizedCrossExtent = float.NaN;

    /// <summary>
    /// Counts viewport range reconciliations. Transform-only scrolling within the current
    /// realized item range does not advance this counter.
    /// </summary>
    public ulong ViewportReconciliationCount { get; private set; }

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

    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation != value)
            {
                _orientation = value;
                OnScrollOffsetChanged(ScrollOffset);
                Invalidate();
            }
        }
    }

    public override float TotalVirtualHeight => Orientation == Orientation.Vertical ? ItemsCount * ItemHeight : ViewportHeight;
    public override float TotalVirtualWidth => Orientation == Orientation.Horizontal ? ItemsCount * ItemWidth : ViewportWidth;
    public override bool IsHorizontal => Orientation == Orientation.Horizontal;

    protected override void OnScrollOffsetChanged(float newOffset)
    {
        _scrollTranslation.Translation = Orientation == Orientation.Horizontal
            ? new Vector2(-newOffset, 0f)
            : new Vector2(0f, -newOffset);
        ChildrenRetainedTransform = ScrollViewerOwner == null ? _scrollTranslation : null;
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
        // The base panel sizes viewport chrome and generically arranges its children to
        // the full virtual extent. Always restore item rectangles after that pass, even
        // when the realized range is unchanged. This does not rebind or recycle items.
        UpdateViewport(forceArrange: true);
    }

    private void UpdateViewport(bool forceArrange = false)
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
            // 1. Calculate visible item range (Vertical)
            int startIdx = (int)Math.Floor(ScrollOffset / ItemHeight);
            int endIdx = (int)Math.Ceiling((ScrollOffset + viewportHeight) / ItemHeight);

            startIdx = Math.Clamp(startIdx, 0, itemsCount - 1);
            endIdx = Math.Clamp(endIdx, 0, itemsCount - 1);

            bool canReuseRealizedRange = CanReuseRealizedRange(startIdx, endIdx, viewportWidth);
            if (canReuseRealizedRange && !forceArrange)
            {
                return;
            }
            if (!canReuseRealizedRange)
            {
                ViewportReconciliationCount++;
            }

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

                float posY = i * ItemHeight;
                float itemWidth = viewportWidth;
                visual.RetainedTransform = null;

                visual.Offset = new Vector2(0f, posY);
                visual.Size = new Vector2(itemWidth, ItemHeight);

                if (visual is LayoutNode childNode)
                {
                    childNode.Measure(new Vector2(itemWidth, ItemHeight));
                    childNode.Arrange(new Rect(0f, posY, itemWidth, ItemHeight));
                }
            }

            SetRealizedRange(startIdx, endIdx, viewportWidth);
        }
        else
        {
            // Horizontal scrolling (ScrollOffset is horizontal)
            int startIdx = (int)Math.Floor(ScrollOffset / ItemWidth);
            int endIdx = (int)Math.Ceiling((ScrollOffset + viewportWidth) / ItemWidth);

            startIdx = Math.Clamp(startIdx, 0, itemsCount - 1);
            endIdx = Math.Clamp(endIdx, 0, itemsCount - 1);

            bool canReuseRealizedRange = CanReuseRealizedRange(startIdx, endIdx, viewportHeight);
            if (canReuseRealizedRange && !forceArrange)
            {
                return;
            }
            if (!canReuseRealizedRange)
            {
                ViewportReconciliationCount++;
            }

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
                float itemHeight = viewportHeight;
                visual.RetainedTransform = null;

                visual.Offset = new Vector2(posX, 0f);
                visual.Size = new Vector2(ItemWidth, itemHeight);

                if (visual is LayoutNode childNode)
                {
                    childNode.Measure(new Vector2(ItemWidth, itemHeight));
                    childNode.Arrange(new Rect(posX, 0f, ItemWidth, itemHeight));
                }
            }

            SetRealizedRange(startIdx, endIdx, viewportHeight);
        }
    }

    private bool CanReuseRealizedRange(int startIndex, int endIndex, float crossExtent) =>
        _realizedStartIndex == startIndex &&
        _realizedEndIndex == endIndex &&
        _realizedOrientation == Orientation &&
        _realizedItemWidth == ItemWidth &&
        _realizedItemHeight == ItemHeight &&
        _realizedCrossExtent == crossExtent &&
        _activeVisuals.Count == endIndex - startIndex + 1;

    private void SetRealizedRange(int startIndex, int endIndex, float crossExtent)
    {
        _realizedStartIndex = startIndex;
        _realizedEndIndex = endIndex;
        _realizedOrientation = Orientation;
        _realizedItemWidth = ItemWidth;
        _realizedItemHeight = ItemHeight;
        _realizedCrossExtent = crossExtent;
    }

    private void ClearActiveToRecycler()
    {
        foreach (var vis in _activeVisuals.Values)
        {
            RemoveChild(vis);
            _recycledVisuals.Push(vis);
        }
        _activeVisuals.Clear();
        _realizedStartIndex = -1;
        _realizedEndIndex = -1;
        _realizedItemWidth = float.NaN;
        _realizedItemHeight = float.NaN;
        _realizedCrossExtent = float.NaN;
    }

    public override void ForceRebind()
    {
        ClearActiveToRecycler();
        base.ForceRebind();
    }
}
