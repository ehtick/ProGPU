using System;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Layout;
using ProGPU.Scene;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Microsoft.UI.Xaml.Controls;

public class UniformVirtualizingGridPanel : VirtualizingPanel, IHitTestBackgroundProvider
{
    private int _itemsCount = 0;
    private float _itemWidth = 80f;
    private float _itemHeight = 80f;
    private readonly SceneTransformHandle _scrollTranslation = new();

    protected override bool UsesRetainedChildTranslation =>
        _usesRetainedItemFragments || ScrollViewerOwner == null;

    public UniformVirtualizingGridPanel()
    {
        _ownerBindVisualCallback = BindOwnerVisual;
        ThemeManager.ThemeChanged += OnThemeManagerChanged;
    }

    private void OnThemeManagerChanged()
    {
        _recycledVisuals.Clear();
        Invalidate();
    }

    // Direct binding fallback backing fields
    private Func<Visual>? _createVisualFactory;
    private Action<Visual, int>? _bindVisualCallback;
    private readonly Action<Visual, int> _ownerBindVisualCallback;

    // Viewport binding properties (automatically hooks into ItemsControl if available)
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

    // Recycler pools (active and inactive)
    private readonly Stack<Visual> _recycledVisuals = new();
    private readonly Dictionary<int, Visual> _activeVisuals = new();
    private readonly List<int> _indicesToRecycle = new();
    private readonly List<Visual> _retainedSlotVisuals = new();
    private int[] _retainedSlotIndices = [];
    private bool _usesRetainedItemFragments;
    private FrameworkElement? _hoveredRetainedItem;
    private int _realizedStartIndex = -1;
    private int _realizedEndIndex = -1;
    private int _realizedColumns = -1;
    private float _realizedItemWidth = float.NaN;
    private float _realizedItemHeight = float.NaN;

    /// <summary>
    /// Counts viewport range reconciliations. Transform-only scrolling within the current
    /// realized row range does not advance this counter.
    /// </summary>
    public ulong ViewportReconciliationCount { get; private set; }
    public int FirstRealizedIndex => _realizedStartIndex;
    public int LastRealizedIndex => _realizedEndIndex;
    public bool UsesRetainedItemFragments => _usesRetainedItemFragments;

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

    public int ColumnsCount => Math.Max(1, (int)Math.Floor(Math.Max(1f, ViewportWidth) / ItemWidth));
    
    public int RowsCount => (int)Math.Ceiling((double)ItemsCount / ColumnsCount);

    public override float TotalVirtualHeight => RowsCount * ItemHeight;

    protected override void OnScrollOffsetChanged(float newOffset)
    {
        _scrollTranslation.Translation = new Vector2(0f, -newOffset);
        ChildrenRetainedTransform = _usesRetainedItemFragments
            ? null
            : ScrollViewerOwner == null ? _scrollTranslation : null;
        UpdateViewport();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        float width = float.IsInfinity(availableSize.X) ? 400f : availableSize.X;
        float height = float.IsInfinity(availableSize.Y) ? TotalVirtualHeight : availableSize.Y;

        // Perform active viewport computation during layout pass
        UpdateViewport();

        return new Vector2(width, height);
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        base.ArrangeOverride(arrangeRect);
        // VirtualizingPanel's base layout must size the viewport chrome, but its generic
        // child arrange temporarily stretches every realized container to the complete
        // virtual extent. Restore stable item rectangles even when the visible range did
        // not change; this is placement-only and must not count as reconciliation.
        UpdateViewport(forceArrange: true);
    }

    private void UpdateViewport(bool forceArrange = false)
    {
        int itemsCount = ItemsCount;
        var createVisual = CreateVisualFactory;
        var itemsControl = ItemsControlOwner;
        var ownerBindVisual = itemsControl?.BindVisualCallback;
        var directBindVisual = _bindVisualCallback;

        float viewportWidth = ViewportWidth;
        float viewportHeight = ViewportHeight;

        if (itemsCount == 0 || createVisual == null ||
            (ownerBindVisual == null && directBindVisual == null) ||
            viewportHeight <= 0 || viewportWidth <= 0)
        {
            ClearActiveToRecycler();
            return;
        }

        int cols = Math.Max(1, (int)Math.Floor(Math.Max(1f, viewportWidth) / ItemWidth));
        int rows = (int)Math.Ceiling((double)itemsCount / cols);

        // 1. Calculate visible item range
        int startRow = (int)Math.Floor(ScrollOffset / ItemHeight);
        int endRow = (int)Math.Ceiling((ScrollOffset + viewportHeight) / ItemHeight);

        startRow = Math.Clamp(startRow, 0, rows - 1);
        endRow = Math.Clamp(endRow, 0, rows - 1);

        int startIdx = startRow * cols;
        int endIdx = Math.Min(itemsCount - 1, (endRow + 1) * cols - 1);

        startIdx = Math.Clamp(startIdx, 0, itemsCount - 1);
        endIdx = Math.Clamp(endIdx, 0, itemsCount - 1);

        int expectedActiveCount = endIdx - startIdx + 1;

        if (!_usesRetainedItemFragments &&
            _activeVisuals.Count == 0 &&
            _retainedSlotVisuals.Count == 0)
        {
            var probe = createVisual();
            if (probe is IRetainedVirtualizedItemFragment)
            {
                _usesRetainedItemFragments = true;
                ExcludeFromParentRetainedTransform = true;
                EnsureRetainedSlotCapacity(
                    GetRetainedSlotCapacity(itemsCount, cols, viewportHeight),
                    createVisual,
                    probe);
            }
            else
            {
                _recycledVisuals.Push(probe);
            }
        }

        if (_usesRetainedItemFragments)
        {
            UpdateRetainedFragmentViewport(
                itemsCount,
                cols,
                rows,
                viewportHeight,
                itemsControl,
                ownerBindVisual,
                directBindVisual,
                createVisual,
                forceArrange);
            return;
        }

        bool canReuseRealizedRange =
            _realizedStartIndex == startIdx &&
            _realizedEndIndex == endIdx &&
            _realizedColumns == cols &&
            _realizedItemWidth == ItemWidth &&
            _realizedItemHeight == ItemHeight &&
            _activeVisuals.Count == expectedActiveCount;
        if (canReuseRealizedRange && !forceArrange)
        {
            return;
        }

        if (!canReuseRealizedRange)
        {
            ViewportReconciliationCount++;
        }

        // 2. Recycle items that scrolled out of view
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
            
            // Remove from rendering children and put in recycler pool
            RemoveChild(vis);
            _recycledVisuals.Push(vis);
        }

        // 3. Position and Bind newly visible items
        for (int i = startIdx; i <= endIdx; i++)
        {
            int row = i / cols;
            int col = i % cols;

            if (!_activeVisuals.TryGetValue(i, out var visual))
            {
                // Grab from pool or allocate new
                visual = _recycledVisuals.Count > 0 ? _recycledVisuals.Pop() : createVisual();
                
                // Bind dataset properties
                if (itemsControl != null)
                {
                    var item = itemsControl.GetItemAt(i);
                    if (item != null)
                    {
                        ownerBindVisual!(visual, item, i);
                    }
                }
                else
                {
                    directBindVisual!(visual, i);
                }
                
                _activeVisuals[i] = visual;
                AddChild(visual);
            }

            // Calculate screen position relative to viewport
            float posX = col * ItemWidth;
            float posY = row * ItemHeight;
            visual.RetainedTransform = null;
            
            // Position child visual node
            visual.Offset = new Vector2(posX, posY);
            visual.Size = new Vector2(ItemWidth, ItemHeight);

            // If child is a LayoutNode, arrange it!
            if (visual is LayoutNode childNode)
            {
                childNode.Measure(new Vector2(ItemWidth, ItemHeight));
                childNode.Arrange(new Rect(posX, posY, ItemWidth, ItemHeight));
            }
        }

        _realizedStartIndex = startIdx;
        _realizedEndIndex = endIdx;
        _realizedColumns = cols;
        _realizedItemWidth = ItemWidth;
        _realizedItemHeight = ItemHeight;
    }

    private int GetRetainedSlotCapacity(int itemsCount, int columns, float viewportHeight)
    {
        int visibleRows = checked((int)MathF.Ceiling(viewportHeight / ItemHeight) + 2);
        return Math.Min(itemsCount, checked(Math.Max(1, visibleRows) * columns));
    }

    private void EnsureRetainedSlotCapacity(int required, Func<Visual> createVisual, Visual? probe = null)
    {
        if (_retainedSlotVisuals.Count >= required)
        {
            return;
        }

        int previousCount = _retainedSlotVisuals.Count;
        Array.Resize(ref _retainedSlotIndices, required);
        Array.Fill(_retainedSlotIndices, -1, previousCount, required - previousCount);
        for (int slot = previousCount; slot < required; slot++)
        {
            Visual visual = slot == previousCount && probe != null ? probe : createVisual();
            if (visual is not IRetainedVirtualizedItemFragment)
            {
                throw new InvalidOperationException(
                    "A retained virtualized item template must return the retained fragment type for every slot.");
            }

            if (visual is FrameworkElement element)
            {
                // Pointer routing stays on the panel because slot visuals intentionally have no
                // mutable layout placement. The panel maps input to the active absolute item.
                element.IsHitTestVisible = false;
            }
            _retainedSlotVisuals.Add(visual);
            AddChild(visual);
        }
    }

    private void UpdateRetainedFragmentViewport(
        int itemsCount,
        int columns,
        int rows,
        float viewportHeight,
        ItemsControl? itemsControl,
        Action<Visual, object, int>? ownerBindVisual,
        Action<Visual, int>? directBindVisual,
        Func<Visual> createVisual,
        bool forceArrange)
    {
        int capacity = GetRetainedSlotCapacity(itemsCount, columns, viewportHeight);
        EnsureRetainedSlotCapacity(capacity, createVisual);

        int windowRows = Math.Max(1, (capacity + columns - 1) / columns);
        int firstRow = Math.Clamp(
            (int)MathF.Floor(ScrollOffset / ItemHeight),
            0,
            Math.Max(0, rows - windowRows));
        int startIndex = firstRow * columns;
        int endIndex = Math.Min(itemsCount - 1, startIndex + capacity - 1);
        bool rangeChanged =
            _realizedStartIndex != startIndex ||
            _realizedEndIndex != endIndex ||
            _realizedColumns != columns ||
            _realizedItemWidth != ItemWidth ||
            _realizedItemHeight != ItemHeight;
        if (!rangeChanged && !forceArrange)
        {
            return;
        }

        if (rangeChanged)
        {
            ViewportReconciliationCount++;
        }

        _activeVisuals.Clear();
        bool fragmentChanged = false;
        for (int index = startIndex; index <= endIndex; index++)
        {
            int slot = index % capacity;
            Visual visual = _retainedSlotVisuals[slot];
            bool rebound = _retainedSlotIndices[slot] != index;
            if (rebound)
            {
                if (itemsControl != null)
                {
                    object? item = itemsControl.GetItemAt(index);
                    if (item != null)
                    {
                        ownerBindVisual!(visual, item, index);
                    }
                }
                else
                {
                    directBindVisual!(visual, index);
                }
                _retainedSlotIndices[slot] = index;
            }

            int row = index / columns;
            int col = index % columns;
            var bounds = new Rect(
                col * ItemWidth,
                row * ItemHeight,
                ItemWidth,
                ItemHeight);
            fragmentChanged |= ((IRetainedVirtualizedItemFragment)visual)
                .UpdateRetainedFragment(bounds);
            _activeVisuals[index] = visual;
        }

        _realizedStartIndex = startIndex;
        _realizedEndIndex = endIndex;
        _realizedColumns = columns;
        _realizedItemWidth = ItemWidth;
        _realizedItemHeight = ItemHeight;

        if (fragmentChanged)
        {
            InvalidateRetainedTransform();
        }
    }

    public bool HasHitTestBackground => _usesRetainedItemFragments;

    public override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if (!_usesRetainedItemFragments)
        {
            return;
        }

        for (int slot = 0; slot < _retainedSlotVisuals.Count; slot++)
        {
            var fragment = ((IRetainedVirtualizedItemFragment)_retainedSlotVisuals[slot]).RetainedFragment;
            context.DrawSceneFragment(fragment, _scrollTranslation);
        }
    }

    public override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        if (_usesRetainedItemFragments)
        {
            UpdateRetainedPointerTarget(e);
        }
        base.OnPointerMoved(e);
    }

    public override void OnPointerEntered(PointerRoutedEventArgs e)
    {
        base.OnPointerEntered(e);
        if (_usesRetainedItemFragments)
        {
            UpdateRetainedPointerTarget(e);
        }
    }

    public override void OnPointerExited(PointerRoutedEventArgs e)
    {
        if (_hoveredRetainedItem != null)
        {
            _hoveredRetainedItem.OnPointerExited(CreateForwardedPointerArgs(e));
            _hoveredRetainedItem = null;
        }
        base.OnPointerExited(e);
    }

    public override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (_usesRetainedItemFragments && GetRetainedItemAt(e.ScreenPosition) is { } target)
        {
            var forwarded = CreateForwardedPointerArgs(e);
            forwarded.Handled = true;
            target.OnPointerPressed(forwarded);
            e.Handled = true;
            return;
        }
        base.OnPointerPressed(e);
    }

    private void UpdateRetainedPointerTarget(PointerRoutedEventArgs e)
    {
        FrameworkElement? target = GetRetainedItemAt(e.ScreenPosition);
        if (ReferenceEquals(target, _hoveredRetainedItem))
        {
            return;
        }
        _hoveredRetainedItem?.OnPointerExited(CreateForwardedPointerArgs(e));
        _hoveredRetainedItem = target;
        _hoveredRetainedItem?.OnPointerEntered(CreateForwardedPointerArgs(e));
    }

    private FrameworkElement? GetRetainedItemAt(Vector2 screenPosition)
    {
        Vector2 local = InputSystem.GetLocalPosition(this, screenPosition);
        float contentX = local.X;
        float contentY = local.Y + ScrollOffset;
        if (contentX < 0f || contentY < 0f || contentX >= ViewportWidth || local.Y >= ViewportHeight)
        {
            return null;
        }

        int columns = Math.Max(1, _realizedColumns);
        int col = (int)MathF.Floor(contentX / ItemWidth);
        int row = (int)MathF.Floor(contentY / ItemHeight);
        int index = checked(row * columns + col);
        return _activeVisuals.TryGetValue(index, out Visual? visual)
            ? visual as FrameworkElement
            : null;
    }

    private static PointerRoutedEventArgs CreateForwardedPointerArgs(PointerRoutedEventArgs source) => new()
    {
        Position = source.Position,
        ScreenPosition = source.ScreenPosition,
        IsLeftButtonPressed = source.IsLeftButtonPressed,
        IsMiddleButtonPressed = source.IsMiddleButtonPressed,
        IsRightButtonPressed = source.IsRightButtonPressed,
        WheelDelta = source.WheelDelta,
        OriginalSource = source.OriginalSource
    };

    private void ClearActiveToRecycler()
    {
        if (_usesRetainedItemFragments)
        {
            _activeVisuals.Clear();
            Array.Fill(_retainedSlotIndices, -1);
            _realizedStartIndex = -1;
            _realizedEndIndex = -1;
            _realizedColumns = -1;
            _realizedItemWidth = float.NaN;
            _realizedItemHeight = float.NaN;
            return;
        }

        foreach (var vis in _activeVisuals.Values)
        {
            RemoveChild(vis);
            _recycledVisuals.Push(vis);
        }
        _activeVisuals.Clear();
        _realizedStartIndex = -1;
        _realizedEndIndex = -1;
        _realizedColumns = -1;
        _realizedItemWidth = float.NaN;
        _realizedItemHeight = float.NaN;
    }

    public override void ForceRebind()
    {
        ClearActiveToRecycler();
        base.ForceRebind();
    }

    public override void RebindVisibleItems()
    {
        var itemsControl = ItemsControlOwner;
        var ownerBindVisual = itemsControl?.BindVisualCallback;
        var directBindVisual = _bindVisualCallback;
        if (ownerBindVisual == null && directBindVisual == null)
        {
            return;
        }

        foreach (var pair in _activeVisuals)
        {
            if (itemsControl != null)
            {
                var item = itemsControl.GetItemAt(pair.Key);
                if (item != null)
                {
                    ownerBindVisual!(pair.Value, item, pair.Key);
                }
            }
            else
            {
                directBindVisual!(pair.Value, pair.Key);
            }

            if (pair.Value is IRetainedVirtualizedItemFragment retained)
            {
                int row = pair.Key / Math.Max(1, _realizedColumns);
                int col = pair.Key % Math.Max(1, _realizedColumns);
                retained.UpdateRetainedFragment(new Rect(
                    col * ItemWidth,
                    row * ItemHeight,
                    ItemWidth,
                    ItemHeight));
            }
        }

        if (_usesRetainedItemFragments)
        {
            InvalidateRetainedTransform();
        }
        else
        {
            Invalidate();
        }
    }
}
