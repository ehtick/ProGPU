namespace ProGPU.Virtualization;

/// <summary>
/// Prefix-sum index for variable-size virtualized items. A Fenwick tree keeps
/// offset/index lookup and individual measurement updates logarithmic while
/// retaining only bounded O(N) numeric state and no element references.
/// </summary>
public sealed class VariableSizeIndex
{
    private float[] _sizes = [];
    private float[] _tree = [0f];
    private bool[] _measured = [];

    public VariableSizeIndex(int count = 0, float estimatedSize = 40f)
    {
        Reset(count, estimatedSize);
    }

    public int Count => _sizes.Length;
    public float EstimatedSize { get; private set; }
    public float TotalSize => PrefixSum(Count);

    /// <summary>Reinitializes N items in O(N) time and O(N) storage.</summary>
    public void Reset(int count, float estimatedSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (!float.IsFinite(estimatedSize) || estimatedSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(estimatedSize));

        EstimatedSize = estimatedSize;
        _sizes = new float[count];
        _measured = new bool[count];
        Array.Fill(_sizes, estimatedSize);
        RebuildTree();
    }

    /// <summary>Changes one measured size in O(log N), returning its signed delta.</summary>
    public float SetMeasuredSize(int index, float size)
    {
        ValidateIndex(index);
        if (!float.IsFinite(size) || size <= 0f)
            throw new ArgumentOutOfRangeException(nameof(size));
        float delta = size - _sizes[index];
        _measured[index] = true;
        if (Math.Abs(delta) <= 0.01f) return 0f;
        _sizes[index] = size;
        Add(index + 1, delta);
        return delta;
    }

    public bool IsMeasured(int index)
    {
        ValidateIndex(index);
        return _measured[index];
    }

    public float GetSize(int index)
    {
        ValidateIndex(index);
        return _sizes[index];
    }

    /// <summary>Returns the leading offset of an item in O(log N).</summary>
    public float GetOffset(int index)
    {
        if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index));
        return PrefixSum(index);
    }

    /// <summary>Returns the item containing an offset in O(log N).</summary>
    public int GetIndexAtOffset(float offset)
    {
        if (Count == 0) return -1;
        if (!float.IsFinite(offset) || offset <= 0f) return 0;
        if (offset >= TotalSize) return Count - 1;

        int index = 0;
        float sum = 0f;
        int bit = HighestPowerOfTwoAtMost(Count);
        while (bit != 0)
        {
            int next = index + bit;
            if (next <= Count && sum + _tree[next] <= offset)
            {
                index = next;
                sum += _tree[next];
            }
            bit >>= 1;
        }
        return Math.Min(index, Count - 1);
    }

    public VirtualizationAnchor CaptureAnchor(float scrollOffset)
    {
        int index = GetIndexAtOffset(scrollOffset);
        return index < 0
            ? default
            : new VirtualizationAnchor(index, Math.Max(0f, scrollOffset - GetOffset(index)));
    }

    public float RestoreAnchor(VirtualizationAnchor anchor)
    {
        if (Count == 0) return 0f;
        int index = Math.Clamp(anchor.Index, 0, Count - 1);
        return GetOffset(index) + Math.Clamp(anchor.OffsetWithinItem, 0f, GetSize(index));
    }

    /// <summary>Inserts estimated items in O(N) time, preserving known measurements.</summary>
    public void InsertRange(int index, int count)
    {
        if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index));
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0) return;
        var sizes = new float[Count + count];
        var measured = new bool[sizes.Length];
        Array.Copy(_sizes, 0, sizes, 0, index);
        Array.Fill(sizes, EstimatedSize, index, count);
        Array.Copy(_sizes, index, sizes, index + count, Count - index);
        Array.Copy(_measured, 0, measured, 0, index);
        Array.Copy(_measured, index, measured, index + count, Count - index);
        _sizes = sizes;
        _measured = measured;
        RebuildTree();
    }

    /// <summary>Removes items in O(N) time, preserving remaining measurements.</summary>
    public void RemoveRange(int index, int count)
    {
        if (index < 0 || count < 0 || index + count > Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (count == 0) return;
        var sizes = new float[Count - count];
        var measured = new bool[sizes.Length];
        Array.Copy(_sizes, 0, sizes, 0, index);
        Array.Copy(_sizes, index + count, sizes, index, Count - index - count);
        Array.Copy(_measured, 0, measured, 0, index);
        Array.Copy(_measured, index + count, measured, index, Count - index - count);
        _sizes = sizes;
        _measured = measured;
        RebuildTree();
    }

    public void InvalidateMeasurement(int index)
    {
        ValidateIndex(index);
        float delta = EstimatedSize - _sizes[index];
        _sizes[index] = EstimatedSize;
        _measured[index] = false;
        if (Math.Abs(delta) > 0.01f) Add(index + 1, delta);
    }

    public void InvalidateAllMeasurements() => Reset(Count, EstimatedSize);

    private float PrefixSum(int count)
    {
        float sum = 0f;
        for (int i = count; i > 0; i -= i & -i) sum += _tree[i];
        return sum;
    }

    private void Add(int treeIndex, float delta)
    {
        for (int i = treeIndex; i <= Count; i += i & -i) _tree[i] += delta;
    }

    private void RebuildTree()
    {
        _tree = new float[Count + 1];
        for (int i = 1; i <= Count; i++)
        {
            _tree[i] += _sizes[i - 1];
            int parent = i + (i & -i);
            if (parent <= Count) _tree[parent] += _tree[i];
        }
    }

    private void ValidateIndex(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
    }

    private static int HighestPowerOfTwoAtMost(int value)
    {
        int result = 1;
        while (result <= value >> 1) result <<= 1;
        return result;
    }
}

public readonly record struct VirtualizationAnchor(int Index, float OffsetWithinItem);
