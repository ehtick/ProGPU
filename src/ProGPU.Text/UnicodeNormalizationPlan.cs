using System.Buffers.Binary;

namespace ProGPU.Text;

/// <summary>
/// Canonical Unicode normalization records generated from the same .NET 10
/// FormD/FormC implementation used by the managed OpenType shaper.
/// </summary>
public static class UnicodeNormalizationPlan
{
    private const uint Magic = 0x4e554750u;
    private static readonly Lazy<Data> s_data = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Triples of code point, scalar-array offset, and fully decomposed scalar count.</summary>
    public static ReadOnlyMemory<uint> DecompositionRecords => s_data.Value.DecompositionRecords;

    /// <summary>Concatenated canonical FormD scalar sequences.</summary>
    public static ReadOnlyMemory<uint> DecompositionScalars => s_data.Value.DecompositionScalars;

    /// <summary>Sorted triples of first scalar, second scalar, and canonical FormC composition.</summary>
    public static ReadOnlyMemory<uint> CompositionRecords => s_data.Value.CompositionRecords;

    private static Data Load()
    {
        const string resourceName = "ProGPU.Text.UnicodeNormalizationData.bin";
        using Stream stream = typeof(UnicodeNormalizationPlan).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded Unicode normalization data '{resourceName}'.");
        if (stream.Length > int.MaxValue) throw new InvalidOperationException("Unicode normalization data is too large.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (bytes.Length < 20 || Read(0) != Magic || Read(4) != 1u)
            throw new InvalidOperationException("Unicode normalization data has an unsupported format.");
        int decompositionCount = checked((int)Read(8));
        int scalarCount = checked((int)Read(12));
        int compositionCount = checked((int)Read(16));
        int expectedLength = checked(20 + decompositionCount * 12 + scalarCount * 4 + compositionCount * 12);
        if (bytes.Length != expectedLength)
            throw new InvalidOperationException("Unicode normalization data is truncated or malformed.");
        int offset = 20;
        uint[] decompositions = Copy(decompositionCount * 3, ref offset);
        uint[] scalars = Copy(scalarCount, ref offset);
        uint[] compositions = Copy(compositionCount * 3, ref offset);
        return new Data(decompositions, scalars, compositions);

        uint Read(int byteOffset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(byteOffset, 4));

        uint[] Copy(int count, ref int byteOffset)
        {
            var result = new uint[count];
            for (var index = 0; index < count; index++, byteOffset += 4)
                result[index] = Read(byteOffset);
            return result;
        }
    }

    private sealed record Data(uint[] DecompositionRecords, uint[] DecompositionScalars, uint[] CompositionRecords);
}
