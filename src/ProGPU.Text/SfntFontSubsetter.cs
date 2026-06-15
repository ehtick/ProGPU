using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ProGPU.Text;

#pragma warning disable IDE0078, IDE0300, IDE0301, IDE0305

#if PROGPU_TEXT_PUBLIC
public
#else
internal
#endif
readonly struct SfntGlyphRemap
{
    public SfntGlyphRemap(ushort sourceGlyphId, ushort subsetGlyphId)
    {
        SourceGlyphId = sourceGlyphId;
        SubsetGlyphId = subsetGlyphId;
    }

    public ushort SourceGlyphId { get; }

    public ushort SubsetGlyphId { get; }
}

#if PROGPU_TEXT_PUBLIC
public
#else
internal
#endif
static class SfntFontSubsetter
{
    private const ushort CompositeMoreComponents = 0x0020;
    private const ushort CompositeArgsAreWords = 0x0001;
    private const ushort CompositeHasScale = 0x0008;
    private const ushort CompositeHasXYScale = 0x0040;
    private const ushort CompositeHasTwoByTwo = 0x0080;
    private const ushort CompositeHasInstructions = 0x0100;
    private const uint CheckSumAdjustment = 0xB1B0AFBA;

    public static bool TryCreateGlyphIdPreservingSubset(
        ReadOnlySpan<byte> fontData,
        int directoryOffset,
        IEnumerable<ushort> glyphs,
        out byte[] subset)
    {
        try
        {
            subset = CreateGlyphIdPreservingSubset(fontData, directoryOffset, glyphs);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or IOException)
        {
            subset = Array.Empty<byte>();
            return false;
        }
    }

    public static byte[] CreateGlyphIdPreservingSubset(
        ReadOnlySpan<byte> fontData,
        int directoryOffset,
        IEnumerable<ushort> glyphs)
    {
        FontFaceData face = FontFaceData.Parse(fontData, directoryOffset);

        if (!face.TryGetTable("head", out byte[] headTable) ||
            !face.TryGetTable("maxp", out byte[] maxpTable) ||
            !face.TryGetTable("loca", out byte[] locaTable) ||
            !face.TryGetTable("glyf", out byte[] glyfTable))
        {
            return BuildSfnt(face.SfntVersion, face.Tables.Where(table => table.Tag != "DSIG"));
        }

        if (headTable.Length < 54 || maxpTable.Length < 6)
        {
            throw new FormatException("Required TrueType subset tables are truncated.");
        }

        ushort glyphCount = ReadUShort(maxpTable, 4);
        if (glyphCount == 0)
        {
            return BuildSfnt(face.SfntVersion, face.Tables.Where(table => table.Tag != "DSIG"));
        }

        short sourceLocaFormat = ReadShort(headTable, 50);
        uint[] sourceGlyphOffsets = ReadLoca(locaTable, glyphCount, sourceLocaFormat);
        bool[] includedGlyphs = CreateIncludedGlyphSet(glyphCount, glyphs);
        IncludeCompositeGlyphDependencies(glyfTable, sourceGlyphOffsets, includedGlyphs);

        GlyphTableSubset glyphSubset = BuildGlyphTableSubset(glyfTable, sourceGlyphOffsets, includedGlyphs);
        byte[] subsetHead = (byte[])headTable.Clone();
        WriteUInt(subsetHead, 8, 0);
        WriteShort(subsetHead, 50, 1);

        var tables = new List<TableData>(face.Tables.Count);
        foreach (TableData table in face.Tables)
        {
            if (table.Tag is "DSIG" or "head" or "loca" or "glyf")
            {
                continue;
            }

            tables.Add(table);
        }

        tables.Add(new TableData("head", subsetHead));
        tables.Add(new TableData("loca", glyphSubset.Loca));
        tables.Add(new TableData("glyf", glyphSubset.Glyf));

        return BuildSfnt(face.SfntVersion, tables);
    }

    public static bool TryCreateCompactSubset(
        ReadOnlySpan<byte> fontData,
        int directoryOffset,
        IEnumerable<ushort> glyphs,
        out byte[] subset,
        out SfntGlyphRemap[] glyphMap)
    {
        try
        {
            subset = CreateCompactSubset(fontData, directoryOffset, glyphs, out glyphMap);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or IOException)
        {
            subset = Array.Empty<byte>();
            glyphMap = Array.Empty<SfntGlyphRemap>();
            return false;
        }
    }

    public static byte[] CreateCompactSubset(
        ReadOnlySpan<byte> fontData,
        int directoryOffset,
        IEnumerable<ushort> glyphs,
        out SfntGlyphRemap[] glyphMap)
    {
        FontFaceData face = FontFaceData.Parse(fontData, directoryOffset);

        if (!face.TryGetTable("head", out byte[] headTable) ||
            !face.TryGetTable("maxp", out byte[] maxpTable) ||
            !face.TryGetTable("loca", out byte[] locaTable) ||
            !face.TryGetTable("glyf", out byte[] glyfTable))
        {
            throw new FormatException("Compact subsetting requires TrueType head, maxp, loca, and glyf tables.");
        }

        if (headTable.Length < 54 || maxpTable.Length < 6)
        {
            throw new FormatException("Required TrueType subset tables are truncated.");
        }

        ushort glyphCount = ReadUShort(maxpTable, 4);
        if (glyphCount == 0)
        {
            throw new FormatException("Compact subsetting requires at least glyph 0.");
        }

        short sourceLocaFormat = ReadShort(headTable, 50);
        uint[] sourceGlyphOffsets = ReadLoca(locaTable, glyphCount, sourceLocaFormat);
        bool[] includedGlyphs = CreateIncludedGlyphSet(glyphCount, glyphs);
        IncludeCompositeGlyphDependencies(glyfTable, sourceGlyphOffsets, includedGlyphs);

        ushort[] sourceGlyphOrder = CreateCompactGlyphOrder(includedGlyphs);
        if (sourceGlyphOrder.Length > ushort.MaxValue)
        {
            throw new FormatException("Compact subset glyph count exceeds UInt16 range.");
        }

        var sourceToSubsetMap = CreateGlyphRemap(sourceGlyphOrder, out glyphMap);
        GlyphTableSubset glyphSubset = BuildCompactGlyphTableSubset(glyfTable, sourceGlyphOffsets, sourceGlyphOrder, sourceToSubsetMap);

        byte[] subsetHead = (byte[])headTable.Clone();
        WriteUInt(subsetHead, 8, 0);
        WriteShort(subsetHead, 50, 1);

        byte[] subsetMaxp = (byte[])maxpTable.Clone();
        WriteUShort(subsetMaxp, 4, checked((ushort)sourceGlyphOrder.Length));

        var hasHorizontalMetrics = false;
        byte[] subsetHhea = Array.Empty<byte>();
        byte[] subsetHmtx = Array.Empty<byte>();
        if (face.TryGetTable("hhea", out byte[] hheaTable) &&
            face.TryGetTable("hmtx", out byte[] hmtxTable))
        {
            if (hheaTable.Length < 36)
            {
                throw new FormatException("Horizontal header table is truncated.");
            }

            subsetHhea = (byte[])hheaTable.Clone();
            WriteUShort(subsetHhea, 34, checked((ushort)sourceGlyphOrder.Length));
            subsetHmtx = BuildCompactHmtxTable(hheaTable, hmtxTable, sourceGlyphOrder);
            hasHorizontalMetrics = true;
        }

        var tables = new List<TableData>(face.Tables.Count);
        foreach (TableData table in face.Tables)
        {
            if (ShouldSkipCompactCopiedTable(table.Tag))
            {
                continue;
            }

            tables.Add(table);
        }

        tables.Add(new TableData("head", subsetHead));
        tables.Add(new TableData("maxp", subsetMaxp));
        tables.Add(new TableData("loca", glyphSubset.Loca));
        tables.Add(new TableData("glyf", glyphSubset.Glyf));
        if (hasHorizontalMetrics)
        {
            tables.Add(new TableData("hhea", subsetHhea));
            tables.Add(new TableData("hmtx", subsetHmtx));
        }

        return BuildSfnt(face.SfntVersion, tables);
    }

    private static bool[] CreateIncludedGlyphSet(ushort glyphCount, IEnumerable<ushort> glyphs)
    {
        var includedGlyphs = new bool[glyphCount];
        includedGlyphs[0] = true;

        if (glyphs == null)
        {
            return includedGlyphs;
        }

        foreach (ushort glyph in glyphs)
        {
            if (glyph < glyphCount)
            {
                includedGlyphs[glyph] = true;
            }
        }

        return includedGlyphs;
    }

    private static void IncludeCompositeGlyphDependencies(
        ReadOnlySpan<byte> glyfTable,
        uint[] sourceGlyphOffsets,
        bool[] includedGlyphs)
    {
        var queue = new Queue<ushort>();
        for (int i = 0; i < includedGlyphs.Length; i++)
        {
            if (includedGlyphs[i])
            {
                queue.Enqueue((ushort)i);
            }
        }

        while (queue.Count > 0)
        {
            ushort glyph = queue.Dequeue();
            foreach (ushort componentGlyph in ReadCompositeComponents(glyfTable, sourceGlyphOffsets, glyph))
            {
                if (componentGlyph < includedGlyphs.Length && !includedGlyphs[componentGlyph])
                {
                    includedGlyphs[componentGlyph] = true;
                    queue.Enqueue(componentGlyph);
                }
            }
        }
    }

    private static ushort[] CreateCompactGlyphOrder(bool[] includedGlyphs)
    {
        var glyphOrder = new List<ushort>();
        for (int i = 0; i < includedGlyphs.Length; i++)
        {
            if (includedGlyphs[i])
            {
                glyphOrder.Add(checked((ushort)i));
            }
        }

        if (glyphOrder.Count == 0 || glyphOrder[0] != 0)
        {
            glyphOrder.Insert(0, 0);
        }

        return glyphOrder.ToArray();
    }

    private static Dictionary<ushort, ushort> CreateGlyphRemap(ushort[] sourceGlyphOrder, out SfntGlyphRemap[] glyphMap)
    {
        var sourceToSubsetMap = new Dictionary<ushort, ushort>(sourceGlyphOrder.Length);
        glyphMap = new SfntGlyphRemap[sourceGlyphOrder.Length];
        for (int subsetGlyph = 0; subsetGlyph < sourceGlyphOrder.Length; subsetGlyph++)
        {
            ushort sourceGlyph = sourceGlyphOrder[subsetGlyph];
            ushort compactGlyph = checked((ushort)subsetGlyph);
            sourceToSubsetMap[sourceGlyph] = compactGlyph;
            glyphMap[subsetGlyph] = new SfntGlyphRemap(sourceGlyph, compactGlyph);
        }

        return sourceToSubsetMap;
    }

    private static List<ushort> ReadCompositeComponents(
        ReadOnlySpan<byte> glyfTable,
        uint[] sourceGlyphOffsets,
        ushort glyph)
    {
        var components = new List<ushort>();
        if (glyph >= sourceGlyphOffsets.Length - 1)
        {
            return components;
        }

        uint glyphStart = sourceGlyphOffsets[glyph];
        uint glyphEnd = sourceGlyphOffsets[glyph + 1];
        if (glyphStart == glyphEnd || glyphStart > glyphEnd || glyphEnd > glyfTable.Length)
        {
            return components;
        }

        int offset = checked((int)glyphStart);
        if (!CanRead(glyfTable, offset, 10) || ReadShort(glyfTable, offset) >= 0)
        {
            return components;
        }

        offset += 10;
        ushort flags;
        do
        {
            if (!CanRead(glyfTable, offset, 4))
            {
                return components;
            }

            flags = ReadUShort(glyfTable, offset);
            components.Add(ReadUShort(glyfTable, offset + 2));
            offset += 4;

            offset += (flags & CompositeArgsAreWords) != 0 ? 4 : 2;
            if ((flags & CompositeHasScale) != 0)
            {
                offset += 2;
            }
            else if ((flags & CompositeHasXYScale) != 0)
            {
                offset += 4;
            }
            else if ((flags & CompositeHasTwoByTwo) != 0)
            {
                offset += 8;
            }
        }
        while ((flags & CompositeMoreComponents) != 0);

        return components;
    }

    private static GlyphTableSubset BuildGlyphTableSubset(
        ReadOnlySpan<byte> sourceGlyf,
        uint[] sourceGlyphOffsets,
        bool[] includedGlyphs)
    {
        var offsets = new uint[sourceGlyphOffsets.Length];
        using var glyfStream = new MemoryStream();

        for (int glyph = 0; glyph < includedGlyphs.Length; glyph++)
        {
            offsets[glyph] = checked((uint)glyfStream.Position);
            if (!includedGlyphs[glyph])
            {
                continue;
            }

            uint sourceStart = sourceGlyphOffsets[glyph];
            uint sourceEnd = sourceGlyphOffsets[glyph + 1];
            if (sourceStart == sourceEnd)
            {
                continue;
            }

            if (sourceStart > sourceEnd || sourceEnd > sourceGlyf.Length)
            {
                throw new FormatException("Glyph location table contains an invalid glyph range.");
            }

            ReadOnlySpan<byte> glyphData = sourceGlyf.Slice(checked((int)sourceStart), checked((int)(sourceEnd - sourceStart)));
            glyfStream.Write(glyphData);
            WritePadding(glyfStream);
        }

        offsets[^1] = checked((uint)glyfStream.Position);
        return new GlyphTableSubset(glyfStream.ToArray(), BuildLongLoca(offsets));
    }

    private static GlyphTableSubset BuildCompactGlyphTableSubset(
        ReadOnlySpan<byte> sourceGlyf,
        uint[] sourceGlyphOffsets,
        ushort[] sourceGlyphOrder,
        IReadOnlyDictionary<ushort, ushort> sourceToSubsetMap)
    {
        var offsets = new uint[sourceGlyphOrder.Length + 1];
        using var glyfStream = new MemoryStream();

        for (int subsetGlyph = 0; subsetGlyph < sourceGlyphOrder.Length; subsetGlyph++)
        {
            offsets[subsetGlyph] = checked((uint)glyfStream.Position);
            ushort sourceGlyph = sourceGlyphOrder[subsetGlyph];
            uint sourceStart = sourceGlyphOffsets[sourceGlyph];
            uint sourceEnd = sourceGlyphOffsets[sourceGlyph + 1];
            if (sourceStart == sourceEnd)
            {
                continue;
            }

            if (sourceStart > sourceEnd || sourceEnd > sourceGlyf.Length)
            {
                throw new FormatException("Glyph location table contains an invalid glyph range.");
            }

            ReadOnlySpan<byte> glyphData = sourceGlyf.Slice(checked((int)sourceStart), checked((int)(sourceEnd - sourceStart)));
            byte[] remappedGlyph = RemapCompositeGlyphData(glyphData, sourceToSubsetMap);
            glyfStream.Write(remappedGlyph);
            WritePadding(glyfStream);
        }

        offsets[^1] = checked((uint)glyfStream.Position);
        return new GlyphTableSubset(glyfStream.ToArray(), BuildLongLoca(offsets));
    }

    private static byte[] RemapCompositeGlyphData(
        ReadOnlySpan<byte> glyphData,
        IReadOnlyDictionary<ushort, ushort> sourceToSubsetMap)
    {
        byte[] remappedGlyph = glyphData.ToArray();
        if (glyphData.Length == 0)
        {
            return remappedGlyph;
        }

        if (!CanRead(glyphData, 0, 10) || ReadShort(glyphData, 0) >= 0)
        {
            return remappedGlyph;
        }

        int offset = 10;
        ushort flags;
        do
        {
            if (!CanRead(glyphData, offset, 4))
            {
                throw new FormatException("Composite glyph component record is truncated.");
            }

            flags = ReadUShort(glyphData, offset);
            ushort sourceGlyph = ReadUShort(glyphData, offset + 2);
            if (!sourceToSubsetMap.TryGetValue(sourceGlyph, out ushort subsetGlyph))
            {
                throw new FormatException("Composite glyph references a glyph outside the compact subset.");
            }

            WriteUShort(remappedGlyph, offset + 2, subsetGlyph);
            offset += 4;

            offset += (flags & CompositeArgsAreWords) != 0 ? 4 : 2;
            if ((flags & CompositeHasScale) != 0)
            {
                offset += 2;
            }
            else if ((flags & CompositeHasXYScale) != 0)
            {
                offset += 4;
            }
            else if ((flags & CompositeHasTwoByTwo) != 0)
            {
                offset += 8;
            }
        }
        while ((flags & CompositeMoreComponents) != 0);

        if ((flags & CompositeHasInstructions) != 0)
        {
            if (!CanRead(glyphData, offset, 2))
            {
                throw new FormatException("Composite glyph instruction length is truncated.");
            }

            int instructionLength = ReadUShort(glyphData, offset);
            if (!CanRead(glyphData, offset + 2, instructionLength))
            {
                throw new FormatException("Composite glyph instructions are truncated.");
            }
        }

        return remappedGlyph;
    }

    private static byte[] BuildCompactHmtxTable(
        ReadOnlySpan<byte> sourceHhea,
        ReadOnlySpan<byte> sourceHmtx,
        ushort[] sourceGlyphOrder)
    {
        ushort sourceMetricCount = ReadUShort(sourceHhea, 34);
        if (sourceMetricCount == 0)
        {
            throw new FormatException("Horizontal metrics table has no long metrics.");
        }

        using var hmtxStream = new MemoryStream(checked(sourceGlyphOrder.Length * 4));
        foreach (ushort sourceGlyph in sourceGlyphOrder)
        {
            ReadSourceHorizontalMetrics(sourceHmtx, sourceMetricCount, sourceGlyph, out ushort advanceWidth, out short leftSideBearing);
            hmtxStream.WriteByte((byte)(advanceWidth >> 8));
            hmtxStream.WriteByte((byte)(advanceWidth & 0xFF));
            hmtxStream.WriteByte((byte)((unchecked((ushort)leftSideBearing) >> 8) & 0xFF));
            hmtxStream.WriteByte((byte)(unchecked((ushort)leftSideBearing) & 0xFF));
        }

        return hmtxStream.ToArray();
    }

    private static void ReadSourceHorizontalMetrics(
        ReadOnlySpan<byte> sourceHmtx,
        ushort sourceMetricCount,
        ushort sourceGlyph,
        out ushort advanceWidth,
        out short leftSideBearing)
    {
        int advanceOffset;
        int leftSideBearingOffset;
        if (sourceGlyph < sourceMetricCount)
        {
            advanceOffset = sourceGlyph * 4;
            leftSideBearingOffset = advanceOffset + 2;
        }
        else
        {
            advanceOffset = (sourceMetricCount - 1) * 4;
            leftSideBearingOffset = sourceMetricCount * 4 + (sourceGlyph - sourceMetricCount) * 2;
        }

        if (!CanRead(sourceHmtx, advanceOffset, 2))
        {
            throw new FormatException("Horizontal metrics table is truncated.");
        }

        advanceWidth = ReadUShort(sourceHmtx, advanceOffset);
        leftSideBearing = CanRead(sourceHmtx, leftSideBearingOffset, 2)
            ? ReadShort(sourceHmtx, leftSideBearingOffset)
            : (short)0;
    }

    private static uint[] ReadLoca(ReadOnlySpan<byte> locaTable, ushort glyphCount, short locaFormat)
    {
        var offsets = new uint[glyphCount + 1];
        if (locaFormat == 0)
        {
            int requiredLength = checked((glyphCount + 1) * 2);
            if (locaTable.Length < requiredLength)
            {
                throw new FormatException("Short glyph location table is truncated.");
            }

            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] = (uint)(ReadUShort(locaTable, i * 2) * 2);
            }
        }
        else if (locaFormat == 1)
        {
            int requiredLength = checked((glyphCount + 1) * 4);
            if (locaTable.Length < requiredLength)
            {
                throw new FormatException("Long glyph location table is truncated.");
            }

            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] = ReadUInt(locaTable, i * 4);
            }
        }
        else
        {
            throw new FormatException("Unsupported glyph location table format.");
        }

        return offsets;
    }

    private static byte[] BuildLongLoca(uint[] offsets)
    {
        var loca = new byte[checked(offsets.Length * 4)];
        for (int i = 0; i < offsets.Length; i++)
        {
            WriteUInt(loca, i * 4, offsets[i]);
        }

        return loca;
    }

    private static byte[] BuildSfnt(uint sfntVersion, IEnumerable<TableData> tables)
    {
        List<TableData> sortedTables = tables
            .GroupBy(table => table.Tag, StringComparer.Ordinal)
            .Select(group => group.Last())
            .OrderBy(table => table.Tag, StringComparer.Ordinal)
            .ToList();

        if (sortedTables.Count == 0 || sortedTables.Count > ushort.MaxValue)
        {
            throw new FormatException("SFNT table count is invalid.");
        }

        ushort tableCount = checked((ushort)sortedTables.Count);
        ushort searchRange = CalculateSearchRange(tableCount);
        ushort entrySelector = CalculateEntrySelector(searchRange);
        ushort rangeShift = checked((ushort)(tableCount * 16 - searchRange));

        int directoryLength = checked(12 + tableCount * 16);
        uint tableOffset = checked((uint)Align4(directoryLength));
        var tableRecords = new List<OutputTableRecord>(tableCount);

        foreach (TableData table in sortedTables)
        {
            tableRecords.Add(new OutputTableRecord(
                table.Tag,
                CalculateChecksum(table.Data),
                tableOffset,
                checked((uint)table.Data.Length),
                table.Data));
            tableOffset = checked(tableOffset + (uint)Align4(table.Data.Length));
        }

        var output = new byte[tableOffset];
        WriteUInt(output, 0, sfntVersion);
        WriteUShort(output, 4, tableCount);
        WriteUShort(output, 6, searchRange);
        WriteUShort(output, 8, entrySelector);
        WriteUShort(output, 10, rangeShift);

        for (int i = 0; i < tableRecords.Count; i++)
        {
            OutputTableRecord table = tableRecords[i];
            int recordOffset = 12 + i * 16;
            WriteTag(output, recordOffset, table.Tag);
            WriteUInt(output, recordOffset + 4, table.Checksum);
            WriteUInt(output, recordOffset + 8, table.Offset);
            WriteUInt(output, recordOffset + 12, table.Length);
            table.Data.CopyTo(output.AsSpan(checked((int)table.Offset)));
        }

        int headRecordIndex = tableRecords.FindIndex(table => table.Tag == "head");
        if (headRecordIndex >= 0 && tableRecords[headRecordIndex].Length >= 12)
        {
            OutputTableRecord headRecord = tableRecords[headRecordIndex];
            int headOffset = checked((int)headRecord.Offset);
            WriteUInt(output, headOffset + 8, 0);
            uint adjustment = unchecked(CheckSumAdjustment - CalculateChecksum(output));
            WriteUInt(output, headOffset + 8, adjustment);
        }

        return output;
    }

    private static bool ShouldSkipCompactCopiedTable(string tag)
    {
        return tag is "DSIG" or "head" or "maxp" or "loca" or "glyf" or "hhea" or "hmtx" or
            "cmap" or "GSUB" or "GPOS" or "GDEF" or "kern" or "vhea" or "vmtx" or
            "VORG" or "BASE" or "JSTF" or "MATH" or "COLR" or "CPAL" or "post";
    }

    private static ushort CalculateSearchRange(ushort tableCount)
    {
        ushort maxPowerOfTwo = 1;
        while (maxPowerOfTwo <= tableCount / 2)
        {
            maxPowerOfTwo *= 2;
        }

        return checked((ushort)(maxPowerOfTwo * 16));
    }

    private static ushort CalculateEntrySelector(ushort searchRange)
    {
        ushort powerOfTwo = checked((ushort)(searchRange / 16));
        ushort entrySelector = 0;
        while (powerOfTwo > 1)
        {
            powerOfTwo /= 2;
            entrySelector++;
        }

        return entrySelector;
    }

    private static int Align4(int value)
    {
        return checked((value + 3) & ~3);
    }

    private static void WritePadding(MemoryStream stream)
    {
        while ((stream.Position & 3) != 0)
        {
            stream.WriteByte(0);
        }
    }

    private static uint CalculateChecksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        int paddedLength = Align4(data.Length);
        for (int offset = 0; offset < paddedLength; offset += 4)
        {
            uint value = 0;
            for (int i = 0; i < 4; i++)
            {
                int index = offset + i;
                value = (value << 8) | (index < data.Length ? data[index] : 0u);
            }

            sum = unchecked(sum + value);
        }

        return sum;
    }

    private static string ReadTag(ReadOnlySpan<byte> data, int offset)
    {
        if (!CanRead(data, offset, 4))
        {
            throw new FormatException("SFNT tag is truncated.");
        }

        return new string(new[]
        {
            (char)data[offset],
            (char)data[offset + 1],
            (char)data[offset + 2],
            (char)data[offset + 3],
        });
    }

    private static ushort ReadUShort(ReadOnlySpan<byte> data, int offset)
    {
        if (!CanRead(data, offset, 2))
        {
            throw new FormatException("SFNT UInt16 value is truncated.");
        }

        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static short ReadShort(ReadOnlySpan<byte> data, int offset)
    {
        return unchecked((short)ReadUShort(data, offset));
    }

    private static uint ReadUInt(ReadOnlySpan<byte> data, int offset)
    {
        if (!CanRead(data, offset, 4))
        {
            throw new FormatException("SFNT UInt32 value is truncated.");
        }

        return (uint)((data[offset] << 24) |
                      (data[offset + 1] << 16) |
                      (data[offset + 2] << 8) |
                       data[offset + 3]);
    }

    private static bool CanRead(ReadOnlySpan<byte> data, int offset, int length)
    {
        return offset >= 0 && length >= 0 && offset <= data.Length && length <= data.Length - offset;
    }

    private static void WriteTag(Span<byte> data, int offset, string tag)
    {
        if (tag.Length != 4)
        {
            throw new FormatException("SFNT table tags must be four characters.");
        }

        for (int i = 0; i < 4; i++)
        {
            data[offset + i] = checked((byte)tag[i]);
        }
    }

    private static void WriteUShort(Span<byte> data, int offset, ushort value)
    {
        data[offset] = (byte)((value >> 8) & 0xFF);
        data[offset + 1] = (byte)(value & 0xFF);
    }

    private static void WriteShort(Span<byte> data, int offset, short value)
    {
        WriteUShort(data, offset, unchecked((ushort)value));
    }

    private static void WriteUInt(Span<byte> data, int offset, uint value)
    {
        data[offset] = (byte)((value >> 24) & 0xFF);
        data[offset + 1] = (byte)((value >> 16) & 0xFF);
        data[offset + 2] = (byte)((value >> 8) & 0xFF);
        data[offset + 3] = (byte)(value & 0xFF);
    }

    private readonly struct GlyphTableSubset
    {
        public GlyphTableSubset(byte[] glyf, byte[] loca)
        {
            Glyf = glyf;
            Loca = loca;
        }

        public byte[] Glyf { get; }

        public byte[] Loca { get; }
    }

    private readonly struct TableData
    {
        public TableData(string tag, byte[] data)
        {
            Tag = tag;
            Data = data;
        }

        public string Tag { get; }

        public byte[] Data { get; }
    }

    private readonly struct OutputTableRecord
    {
        public OutputTableRecord(string tag, uint checksum, uint offset, uint length, byte[] data)
        {
            Tag = tag;
            Checksum = checksum;
            Offset = offset;
            Length = length;
            Data = data;
        }

        public string Tag { get; }

        public uint Checksum { get; }

        public uint Offset { get; }

        public uint Length { get; }

        public byte[] Data { get; }
    }

    private sealed class FontFaceData
    {
        private readonly Dictionary<string, TableData> _tables;

        private FontFaceData(uint sfntVersion, Dictionary<string, TableData> tables)
        {
            SfntVersion = sfntVersion;
            _tables = tables;
        }

        public uint SfntVersion { get; }

        public Dictionary<string, TableData>.ValueCollection Tables => _tables.Values;

        public bool TryGetTable(string tag, out byte[] table)
        {
            if (_tables.TryGetValue(tag, out TableData tableData))
            {
                table = tableData.Data;
                return true;
            }

            table = Array.Empty<byte>();
            return false;
        }

        public static FontFaceData Parse(ReadOnlySpan<byte> fontData, int directoryOffset)
        {
            if (directoryOffset < 0 || directoryOffset > fontData.Length || fontData.Length - directoryOffset < 12)
            {
                throw new ArgumentOutOfRangeException(nameof(directoryOffset));
            }

            uint sfntVersion = ReadUInt(fontData, directoryOffset);
            ushort tableCount = ReadUShort(fontData, directoryOffset + 4);
            int tableDirectoryOffset = checked(directoryOffset + 12);
            int tableDirectoryLength = checked(tableCount * 16);
            if (!CanRead(fontData, tableDirectoryOffset, tableDirectoryLength))
            {
                throw new FormatException("SFNT table directory is truncated.");
            }

            var tables = new Dictionary<string, TableData>(StringComparer.Ordinal);
            for (int i = 0; i < tableCount; i++)
            {
                int recordOffset = tableDirectoryOffset + i * 16;
                string tag = ReadTag(fontData, recordOffset);
                uint tableOffset = ReadUInt(fontData, recordOffset + 8);
                uint tableLength = ReadUInt(fontData, recordOffset + 12);
                if (tableOffset > int.MaxValue || tableLength > int.MaxValue)
                {
                    throw new FormatException("SFNT table offset or length is too large.");
                }

                if (!CanRead(fontData, checked((int)tableOffset), checked((int)tableLength)))
                {
                    throw new FormatException("SFNT table data is outside the font data.");
                }

                tables[tag] = new TableData(
                    tag,
                    fontData.Slice(checked((int)tableOffset), checked((int)tableLength)).ToArray());
            }

            return new FontFaceData(sfntVersion, tables);
        }
    }
}
