using System.Text;

if (args.Length != 1)
    throw new ArgumentException("Usage: ProGPU.UnicodeNormalizationGenerator <output.bin>");

var decompositions = new List<(uint CodePoint, int Offset, int Count)>();
var scalars = new List<uint>();
var compositions = new Dictionary<ulong, uint>();
for (uint codePoint = 0; codePoint <= 0x10ffffu; codePoint++)
{
    if (codePoint is >= 0xd800u and <= 0xdfffu) continue;
    string source = new Rune(checked((int)codePoint)).ToString();
    string normalized;
    try { normalized = source.Normalize(NormalizationForm.FormD); }
    catch (ArgumentException) { continue; }
    if (normalized.Equals(source, StringComparison.Ordinal)) continue;
    uint[] components = normalized.EnumerateRunes().Select(static rune => (uint)rune.Value).ToArray();
    decompositions.Add((codePoint, scalars.Count, components.Length));
    scalars.AddRange(components);

    uint composed = components[0];
    for (var index = 1; index < components.Length; index++)
    {
        string pair = new Rune(checked((int)composed)).ToString() +
            new Rune(checked((int)components[index])).ToString();
        Rune[] result = pair.Normalize(NormalizationForm.FormC).EnumerateRunes().ToArray();
        if (result.Length == 1)
        {
            uint next = (uint)result[0].Value;
            compositions[((ulong)composed << 32) | components[index]] = next;
            composed = next;
        }
    }
}

using var stream = File.Create(args[0]);
using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
writer.Write(0x4e554750u);
writer.Write(1u);
writer.Write(checked((uint)decompositions.Count));
writer.Write(checked((uint)scalars.Count));
writer.Write(checked((uint)compositions.Count));
foreach ((uint codePoint, int offset, int count) in decompositions)
{
    writer.Write(codePoint);
    writer.Write(checked((uint)offset));
    writer.Write(checked((uint)count));
}
foreach (uint value in scalars) writer.Write(value);
foreach ((ulong key, uint composed) in compositions.OrderBy(static item => item.Key))
{
    writer.Write((uint)(key >> 32));
    writer.Write((uint)key);
    writer.Write(composed);
}
