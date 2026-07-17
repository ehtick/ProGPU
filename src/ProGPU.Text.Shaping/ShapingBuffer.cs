using System.Buffers;

namespace ProGPU.Text.Shaping;

/// <summary>
/// A reusable, pool-backed glyph buffer shared by the CPU and WebGPU shaping
/// frontends. It stores value-only records and performs no allocation while its
/// capacity is sufficient.
/// </summary>
public sealed class ShapingBuffer : IDisposable
{
    public const int DefaultMaximumGlyphCount = 1_048_576;

    private ShapingGlyph[]? _glyphs;
    private readonly int _maximumGlyphCount;

    public ShapingBuffer(int initialCapacity = 32, int maximumGlyphCount = DefaultMaximumGlyphCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumGlyphCount);
        if (initialCapacity > maximumGlyphCount)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity), "Initial capacity exceeds the glyph limit.");
        }

        _maximumGlyphCount = maximumGlyphCount;
        _glyphs = initialCapacity == 0
            ? Array.Empty<ShapingGlyph>()
            : ArrayPool<ShapingGlyph>.Shared.Rent(initialCapacity);
    }

    public int Count { get; private set; }
    public int Capacity => GetStorage().Length;
    public int MaximumGlyphCount => _maximumGlyphCount;
    public ReadOnlySpan<ShapingGlyph> Glyphs => GetStorage().AsSpan(0, Count);
    public Span<ShapingGlyph> WritableGlyphs => GetStorage().AsSpan(0, Count);

    public ref ShapingGlyph this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return ref GetStorage()[index];
        }
    }

    public void Clear()
    {
        _ = GetStorage();
        Count = 0;
    }

    public void EnsureCapacity(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        ShapingGlyph[] current = GetStorage();
        if (capacity > _maximumGlyphCount)
        {
            throw new InvalidOperationException($"Shaping would exceed the configured {_maximumGlyphCount} glyph limit.");
        }
        if (capacity <= current.Length) return;

        int newCapacity = Math.Min(
            _maximumGlyphCount,
            Math.Max(capacity, Math.Max(32, checked(current.Length * 2))));
        ShapingGlyph[] replacement = ArrayPool<ShapingGlyph>.Shared.Rent(newCapacity);
        current.AsSpan(0, Count).CopyTo(replacement);
        if (current.Length != 0) ArrayPool<ShapingGlyph>.Shared.Return(current);
        _glyphs = replacement;
    }

    public void Append(in ShapingGlyph glyph)
    {
        EnsureCapacity(checked(Count + 1));
        GetStorage()[Count++] = glyph;
    }

    public void Append(ReadOnlySpan<ShapingGlyph> glyphs)
    {
        if (glyphs.IsEmpty)
        {
            _ = GetStorage();
            return;
        }
        int newCount = checked(Count + glyphs.Length);
        EnsureCapacity(newCount);
        glyphs.CopyTo(GetStorage().AsSpan(Count));
        Count = newCount;
    }

    public void Replace(int index, int removeCount, ReadOnlySpan<ShapingGlyph> replacement)
    {
        if ((uint)index > (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (removeCount < 0 || removeCount > Count - index) throw new ArgumentOutOfRangeException(nameof(removeCount));

        int newCount = checked(Count - removeCount + replacement.Length);
        EnsureCapacity(newCount);
        ShapingGlyph[] storage = GetStorage();
        int sourceTail = index + removeCount;
        int targetTail = index + replacement.Length;
        storage.AsSpan(sourceTail, Count - sourceTail).CopyTo(storage.AsSpan(targetTail));
        replacement.CopyTo(storage.AsSpan(index));
        Count = newCount;
    }

    public void Dispose()
    {
        ShapingGlyph[]? glyphs = Interlocked.Exchange(ref _glyphs, null);
        Count = 0;
        if (glyphs is { Length: > 0 })
        {
            ArrayPool<ShapingGlyph>.Shared.Return(glyphs);
        }
    }

    private ShapingGlyph[] GetStorage() =>
        _glyphs ?? throw new ObjectDisposedException(nameof(ShapingBuffer));
}
