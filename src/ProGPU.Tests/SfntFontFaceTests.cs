using System.Text;
using ProGPU.Text;
using Xunit;

namespace ProGPU.Tests;

public class SfntFontFaceTests
{
    [Fact]
    public void ReadsNamesFromSfntNameTable()
    {
        byte[] fontData = BuildSfnt("ProGPU Sans", "ProGPU Sans Regular");

        SfntFontFace face = SfntFontFace.Load(fontData);

        Assert.True(face.TryGetName(SfntNameIds.FamilyName, out string familyName));
        Assert.True(face.TryGetName(SfntNameIds.FullName, out string fullName));
        Assert.Equal("ProGPU Sans", familyName);
        Assert.Equal("ProGPU Sans Regular", fullName);
        Assert.True(face.TryGetTable("name", out ReadOnlyMemory<byte> nameTable));
        Assert.NotEqual(0, nameTable.Length);
    }

    [Fact]
    public void FontApiParsesFontInfoWithFaceIndex()
    {
        string file = Path.Combine(Path.GetTempPath(), $"progpu-font-{Guid.NewGuid():N}.ttf");
        File.WriteAllBytes(file, BuildSfnt("ProGPU Serif", "ProGPU Serif Bold"));

        try
        {
            FontInfo? info = FontApi.ParseFontInfo(file);

            Assert.NotNull(info);
            Assert.Equal("ProGPU Serif", info.FamilyName);
            Assert.Equal("ProGPU Serif Bold", info.Name);
            Assert.Equal(0, info.FaceIndex);
            Assert.Equal(file, info.FilePath);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void LoadsTrueTypeCollectionFaces()
    {
        byte[] fontData = BuildTtc(
            ("ProGPU Mono", "ProGPU Mono Regular"),
            ("ProGPU Mono", "ProGPU Mono Bold"));

        IReadOnlyList<SfntFontFace> faces = SfntFontFace.LoadFaces(fontData);

        Assert.Equal(2, faces.Count);
        Assert.Equal(0, faces[0].FaceIndex);
        Assert.Equal(1, faces[1].FaceIndex);
        Assert.True(faces[0].TryGetName(SfntNameIds.FullName, out string firstName));
        Assert.True(faces[1].TryGetName(SfntNameIds.FullName, out string secondName));
        Assert.Equal("ProGPU Mono Regular", firstName);
        Assert.Equal("ProGPU Mono Bold", secondName);
    }

    [Fact]
    public void TtfFontLoadsRequestedCollectionFace()
    {
        byte[] fontData = BuildMetricsTtc(1000, 2048);

        var first = new TtfFont(fontData, 0);
        var second = new TtfFont(fontData, 1);

        Assert.Equal(0, first.FaceIndex);
        Assert.Equal(1000, first.UnitsPerEm);
        Assert.Equal(1, second.FaceIndex);
        Assert.Equal(2048, second.UnitsPerEm);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TtfFont(fontData, 2));
    }

    [Fact]
    public void ReadsCmapMetricsGlyphBoundsAndEmbeddingRights()
    {
        byte[] fontData = BuildMetricsSfnt();

        SfntFontFace face = SfntFontFace.Load(fontData);

        Assert.False(face.UsesSymbolCharacterMap);
        Assert.True(face.TryGetGlyphCount(out ushort glyphCount));
        Assert.Equal(2, glyphCount);
        Assert.True(face.TryGetGlyphIndex('A', out ushort glyphIndex));
        Assert.Equal(1, glyphIndex);
        Assert.True(face.TryGetHorizontalGlyphMetrics(glyphIndex, out SfntHorizontalGlyphMetrics metrics));
        Assert.Equal(600, metrics.AdvanceWidth);
        Assert.Equal(-20, metrics.LeftSideBearing);
        Assert.True(face.TryGetGlyphBounds(glyphIndex, out SfntGlyphBounds bounds));
        Assert.Equal(-10, bounds.XMin);
        Assert.Equal(-20, bounds.YMin);
        Assert.Equal(300, bounds.XMax);
        Assert.Equal(700, bounds.YMax);
        Assert.True(face.TryGetEmbeddingRights(out ushort fsType));
        Assert.Equal(0x0008, fsType);
    }

    [Fact]
    public void FallsBackToFormat4WhenFormat12MissesBmpGlyph()
    {
        byte[] fontData = BuildSfntWithTables(
            ("head", BuildHeadTable()),
            ("hhea", BuildHheaTable()),
            ("maxp", BuildMaxpTable()),
            ("hmtx", BuildHmtxTable()),
            ("cmap", BuildCmapFormat4And12Table()),
            ("loca", BuildLocaTable()),
            ("glyf", BuildGlyfTable()),
            ("OS/2", BuildOs2Table()));

        SfntFontFace face = SfntFontFace.Load(fontData);

        Assert.True(face.TryGetGlyphIndex('A', out ushort glyphIndex));
        Assert.Equal(1, glyphIndex);
    }

    private static byte[] BuildSfnt(string familyName, string fullName)
    {
        byte[] nameTable = BuildNameTable(familyName, fullName);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteSfntFace(writer, 0, nameTable);
        return stream.ToArray();
    }

    private static byte[] BuildTtc(params (string FamilyName, string FullName)[] faces)
    {
        byte[][] nameTables = faces
            .Select(face => BuildNameTable(face.FamilyName, face.FullName))
            .ToArray();

        uint[] faceOffsets = new uint[faces.Length];
        uint offset = (uint)(12 + faces.Length * 4);
        for (int i = 0; i < faces.Length; i++)
        {
            faceOffsets[i] = offset;
            offset += (uint)(28 + nameTables[i].Length);
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteTag(writer, "ttcf");
        WriteUInt(writer, 0x00010000);
        WriteUInt(writer, (uint)faces.Length);
        foreach (uint faceOffset in faceOffsets)
        {
            WriteUInt(writer, faceOffset);
        }

        for (int i = 0; i < faces.Length; i++)
        {
            stream.Position = faceOffsets[i];
            WriteSfntFace(writer, faceOffsets[i], nameTables[i]);
        }

        return stream.ToArray();
    }

    private static void WriteSfntFace(BinaryWriter writer, uint faceOffset, byte[] nameTable)
    {
        uint nameTableOffset = faceOffset + 28;
        WriteUInt(writer, 0x00010000);
        WriteUShort(writer, 1);
        WriteUShort(writer, 0);
        WriteUShort(writer, 0);
        WriteUShort(writer, 0);
        WriteTag(writer, "name");
        WriteUInt(writer, 0);
        WriteUInt(writer, nameTableOffset);
        WriteUInt(writer, (uint)nameTable.Length);
        writer.Write(nameTable);
    }

    private static byte[] BuildNameTable(string familyName, string fullName)
    {
        byte[] familyBytes = Encoding.BigEndianUnicode.GetBytes(familyName);
        byte[] fullBytes = Encoding.BigEndianUnicode.GetBytes(fullName);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteUShort(writer, 0);
        WriteUShort(writer, 2);
        WriteUShort(writer, 30);
        WriteNameRecord(writer, SfntNameIds.FamilyName, familyBytes.Length, 0);
        WriteNameRecord(writer, SfntNameIds.FullName, fullBytes.Length, familyBytes.Length);
        writer.Write(familyBytes);
        writer.Write(fullBytes);

        return stream.ToArray();
    }

    private static byte[] BuildMetricsSfnt()
    {
        return BuildSfntWithTables(
            ("head", BuildHeadTable()),
            ("hhea", BuildHheaTable()),
            ("maxp", BuildMaxpTable()),
            ("hmtx", BuildHmtxTable()),
            ("cmap", BuildCmapFormat4Table()),
            ("loca", BuildLocaTable()),
            ("glyf", BuildGlyfTable()),
            ("OS/2", BuildOs2Table()));
    }

    private static byte[] BuildMetricsTtc(params ushort[] unitsPerEmValues)
    {
        var faceTables = unitsPerEmValues
            .Select(unitsPerEm => new (string Tag, byte[] Data)[]
            {
                ("head", BuildHeadTable(unitsPerEm)),
                ("hhea", BuildHheaTable()),
                ("maxp", BuildMaxpTable()),
                ("hmtx", BuildHmtxTable()),
                ("cmap", BuildCmapFormat4Table()),
                ("loca", BuildLocaTable()),
                ("glyf", BuildGlyfTable()),
                ("OS/2", BuildOs2Table())
            })
            .ToArray();

        uint[] faceOffsets = new uint[faceTables.Length];
        uint offset = (uint)(12 + faceTables.Length * 4);
        for (int i = 0; i < faceTables.Length; i++)
        {
            faceOffsets[i] = offset;
            offset += GetSfntLength(faceTables[i]);
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteTag(writer, "ttcf");
        WriteUInt(writer, 0x00010000);
        WriteUInt(writer, (uint)faceTables.Length);
        foreach (uint faceOffset in faceOffsets)
        {
            WriteUInt(writer, faceOffset);
        }

        for (int i = 0; i < faceTables.Length; i++)
        {
            stream.Position = faceOffsets[i];
            WriteSfntWithTables(writer, faceOffsets[i], faceTables[i]);
        }

        return stream.ToArray();
    }

    private static byte[] BuildSfntWithTables(params (string Tag, byte[] Data)[] tables)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteUInt(writer, 0x00010000);
        WriteUShort(writer, (ushort)tables.Length);
        WriteUShort(writer, 0);
        WriteUShort(writer, 0);
        WriteUShort(writer, 0);

        uint tableOffset = (uint)(12 + tables.Length * 16);
        foreach ((string tag, byte[] data) in tables)
        {
            WriteTag(writer, tag);
            WriteUInt(writer, 0);
            WriteUInt(writer, tableOffset);
            WriteUInt(writer, (uint)data.Length);
            tableOffset += (uint)data.Length;
        }

        foreach ((_, byte[] data) in tables)
        {
            writer.Write(data);
        }

        return stream.ToArray();
    }

    private static uint GetSfntLength((string Tag, byte[] Data)[] tables)
    {
        return (uint)(12 + tables.Length * 16 + tables.Sum(table => table.Data.Length));
    }

    private static void WriteSfntWithTables(BinaryWriter writer, uint faceOffset, (string Tag, byte[] Data)[] tables)
    {
        WriteUInt(writer, 0x00010000);
        WriteUShort(writer, (ushort)tables.Length);
        WriteUShort(writer, 0);
        WriteUShort(writer, 0);
        WriteUShort(writer, 0);

        uint tableOffset = faceOffset + (uint)(12 + tables.Length * 16);
        foreach ((string tag, byte[] data) in tables)
        {
            WriteTag(writer, tag);
            WriteUInt(writer, 0);
            WriteUInt(writer, tableOffset);
            WriteUInt(writer, (uint)data.Length);
            tableOffset += (uint)data.Length;
        }

        foreach ((_, byte[] data) in tables)
        {
            writer.Write(data);
        }
    }

    private static byte[] BuildHeadTable(ushort unitsPerEm = 1000)
    {
        byte[] table = new byte[54];
        using var stream = new MemoryStream(table);
        using var writer = new BinaryWriter(stream);

        stream.Position = 4;
        WriteUInt(writer, 0x00010000);
        stream.Position = 18;
        WriteUShort(writer, unitsPerEm);
        stream.Position = 50;
        WriteShort(writer, 0);
        return table;
    }

    private static byte[] BuildHheaTable()
    {
        byte[] table = new byte[36];
        using var stream = new MemoryStream(table);
        using var writer = new BinaryWriter(stream);

        stream.Position = 4;
        WriteShort(writer, 800);
        WriteShort(writer, -200);
        WriteShort(writer, 50);
        stream.Position = 34;
        WriteUShort(writer, 2);
        return table;
    }

    private static byte[] BuildMaxpTable()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteUInt(writer, 0x00010000);
        WriteUShort(writer, 2);
        return stream.ToArray();
    }

    private static byte[] BuildHmtxTable()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteUShort(writer, 500);
        WriteShort(writer, 0);
        WriteUShort(writer, 600);
        WriteShort(writer, -20);
        return stream.ToArray();
    }

    private static byte[] BuildCmapFormat4Table()
    {
        byte[] format4 = BuildFormat4Subtable();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteUShort(writer, 0);
        WriteUShort(writer, 1);
        WriteUShort(writer, 3);
        WriteUShort(writer, 1);
        WriteUInt(writer, 12);
        writer.Write(format4);
        return stream.ToArray();
    }

    private static byte[] BuildCmapFormat4And12Table()
    {
        byte[] format4 = BuildFormat4Subtable();
        byte[] format12 = BuildFormat12Subtable();
        uint format4Offset = 20;
        uint format12Offset = format4Offset + (uint)format4.Length;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteUShort(writer, 0);
        WriteUShort(writer, 2);
        WriteUShort(writer, 3);
        WriteUShort(writer, 1);
        WriteUInt(writer, format4Offset);
        WriteUShort(writer, 3);
        WriteUShort(writer, 10);
        WriteUInt(writer, format12Offset);
        writer.Write(format4);
        writer.Write(format12);
        return stream.ToArray();
    }

    private static byte[] BuildFormat4Subtable()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteUShort(writer, 4);
        WriteUShort(writer, 32);
        WriteUShort(writer, 0);
        WriteUShort(writer, 4);
        WriteUShort(writer, 0);
        WriteUShort(writer, 0);
        WriteUShort(writer, 0);
        WriteUShort(writer, 0x0041);
        WriteUShort(writer, 0xFFFF);
        WriteUShort(writer, 0);
        WriteUShort(writer, 0x0041);
        WriteUShort(writer, 0xFFFF);
        WriteShort(writer, -64);
        WriteShort(writer, 1);
        WriteUShort(writer, 0);
        WriteUShort(writer, 0);
        return stream.ToArray();
    }

    private static byte[] BuildFormat12Subtable()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteUShort(writer, 12);
        WriteUShort(writer, 0);
        WriteUInt(writer, 28);
        WriteUInt(writer, 0);
        WriteUInt(writer, 1);
        WriteUInt(writer, 0x1F600);
        WriteUInt(writer, 0x1F600);
        WriteUInt(writer, 1);
        return stream.ToArray();
    }

    private static byte[] BuildLocaTable()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteUShort(writer, 0);
        WriteUShort(writer, 0);
        WriteUShort(writer, 5);
        return stream.ToArray();
    }

    private static byte[] BuildGlyfTable()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteShort(writer, 1);
        WriteShort(writer, -10);
        WriteShort(writer, -20);
        WriteShort(writer, 300);
        WriteShort(writer, 700);
        return stream.ToArray();
    }

    private static byte[] BuildOs2Table()
    {
        byte[] table = new byte[64];
        using var stream = new MemoryStream(table);
        using var writer = new BinaryWriter(stream);

        stream.Position = 4;
        WriteUShort(writer, 400);
        WriteUShort(writer, 5);
        WriteUShort(writer, 0x0008);
        return table;
    }

    private static void WriteNameRecord(BinaryWriter writer, ushort nameId, int length, int offset)
    {
        WriteUShort(writer, 3);
        WriteUShort(writer, 1);
        WriteUShort(writer, 0x0409);
        WriteUShort(writer, nameId);
        WriteUShort(writer, (ushort)length);
        WriteUShort(writer, (ushort)offset);
    }

    private static void WriteTag(BinaryWriter writer, string tag)
    {
        writer.Write(Encoding.ASCII.GetBytes(tag));
    }

    private static void WriteUShort(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteShort(BinaryWriter writer, short value)
    {
        WriteUShort(writer, unchecked((ushort)value));
    }

    private static void WriteUInt(BinaryWriter writer, uint value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }
}
