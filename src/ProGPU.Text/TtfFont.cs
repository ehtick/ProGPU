using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using ProGPU.Vector;

namespace ProGPU.Text;

[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct GpuSegment
{
    public Vector2 P0;
    public Vector2 P1;
    public Vector2 P2;
    public uint SegmentType;
    public uint Pad;
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct GpuGlyphRecord
{
    public uint StartSegment;
    public uint SegmentCount;
    public float MinX;
    public float MinY;
    public float MaxX;
    public float MaxY;
    public uint Pad0;
    public uint Pad1;
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct GlyphUniforms
{
    public float XStart;
    public float YStart;
    public float Scale;
    public uint GlyphIndex;
    
    public uint AtlasX;
    public uint AtlasY;
    public uint Width;
    public uint Height;
    
    public float SubpixelX;
    public float Pad0;
    public float Pad1;
    public float Pad2;
}

public readonly struct BitmapGlyphData
{
    public BitmapGlyphData(
        ushort pixelsPerEm,
        ushort pixelsPerInch,
        short originOffsetX,
        short originOffsetY,
        uint graphicType,
        ReadOnlyMemory<byte> data)
    {
        PixelsPerEm = pixelsPerEm;
        PixelsPerInch = pixelsPerInch;
        OriginOffsetX = originOffsetX;
        OriginOffsetY = originOffsetY;
        GraphicType = graphicType;
        Data = data;
    }

    public ushort PixelsPerEm { get; }
    public ushort PixelsPerInch { get; }
    public short OriginOffsetX { get; }
    public short OriginOffsetY { get; }
    public uint GraphicType { get; }
    public ReadOnlyMemory<byte> Data { get; }
}

public class TtfFont
{
    public const uint JpegBitmapGraphicType = 0x6A706720;
    public const uint PngBitmapGraphicType = 0x706E6720;
    public const uint TiffBitmapGraphicType = 0x74696666;

    private readonly byte[] _data;
    private readonly SfntFontFace _face;
    private readonly Dictionary<string, (uint offset, uint length)> _tables = new();

    public int FaceIndex { get; }
    public string FamilyName { get; }
    public string SubfamilyName { get; }
    public string FullName { get; }
    public ushort WeightClass { get; private set; } = 400;
    public ushort WidthClass { get; private set; } = 5;
    public bool IsItalic { get; private set; }
    public bool HasTrueTypeOutlines { get; private set; }
    public bool HasBitmapGlyphs => _tables.ContainsKey("sbix");
    public bool UsesSymbolCharacterMap => _face.UsesSymbolCharacterMap;

    // Font parameters
    public ushort UnitsPerEm { get; private set; }
    public short Ascender { get; private set; }
    public short Descender { get; private set; }
    public short LineGap { get; private set; }
    public ushort NumGlyphs { get; private set; }
    private short _indexToLocFormat; // 0 = short (16-bit), 1 = long (32-bit)

    // hmtx metrics
    private ushort _numberOfHMetrics;
    private uint _hmtxOffset;

    // loca and glyf offsets
    private uint _locaOffset;
    private uint _glyfOffset;

    // COLR & CPAL color tables variables
    private uint _colrOffset;
    private uint _cpalOffset;

    // CPAL state
    private ushort _numPaletteEntries;
    private ushort _numPalettes;
    private ushort _numColorRecords;
    private uint _colorRecordsOffset;
    private Vector4[] _colorPalette = null!;

    // COLR state
    private ushort _numBaseGlyphRecords;
    private uint _baseGlyphRecordsOffset;
    private uint _layerRecordsOffset;
    private ushort _numLayerRecords;

    public TtfFont(byte[] fontData)
        : this(fontData, 0)
    {
    }

    public TtfFont(byte[] fontData, int faceIndex)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
        _data = fontData;
        FaceIndex = faceIndex;
        _face = SfntFontFace.Load(fontData, faceIndex);
        FamilyName = GetName(SfntNameIds.PreferredFamilyName, SfntNameIds.FamilyName) ?? string.Empty;
        SubfamilyName = GetName(SfntNameIds.PreferredSubfamilyName, SfntNameIds.SubfamilyName) ?? string.Empty;
        FullName = GetName(SfntNameIds.FullName) ?? FamilyName;
        ParseTableDirectory();
        ParseHeadTable();
        ParseHheaTable();
        ParseMaxpTable();
        ParseFontAttributes();
        ParseColrTable();
        ParseCpalTable();
    }

    public TtfFont(string filePath) : this(File.ReadAllBytes(filePath))
    {
    }

    public TtfFont(string filePath, int faceIndex) : this(File.ReadAllBytes(filePath), faceIndex)
    {
    }

    #region Big-Endian Readers
    private ushort ReadUShort(uint offset)
    {
        return (ushort)((_data[offset] << 8) | _data[offset + 1]);
    }

    private short ReadShort(uint offset)
    {
        return (short)((_data[offset] << 8) | _data[offset + 1]);
    }

    private uint ReadUInt(uint offset)
    {
        return (uint)((_data[offset] << 24) | 
                      (_data[offset + 1] << 16) | 
                      (_data[offset + 2] << 8) | 
                      _data[offset + 3]);
    }

    private static ushort ReadUShort(ReadOnlySpan<byte> data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static short ReadShort(ReadOnlySpan<byte> data, int offset)
    {
        return unchecked((short)ReadUShort(data, offset));
    }

    private static uint ReadUInt(ReadOnlySpan<byte> data, int offset)
    {
        return (uint)((data[offset] << 24) |
                      (data[offset + 1] << 16) |
                      (data[offset + 2] << 8) |
                      data[offset + 3]);
    }
    #endregion

    private void ParseTableDirectory()
    {
        foreach (var (tag, record) in _face.Tables)
        {
            _tables[tag] = (record.Offset, record.Length);
        }

        if (!_tables.ContainsKey("head") || !_tables.ContainsKey("cmap"))
        {
            throw new FormatException("Font file is missing essential SFNT tables (head or cmap).");
        }

        if (_tables.TryGetValue("loca", out var loca) && _tables.TryGetValue("glyf", out var glyf))
        {
            _locaOffset = loca.offset;
            _glyfOffset = glyf.offset;
            HasTrueTypeOutlines = true;
        }
    }

    private string? GetName(params ushort[] nameIds)
    {
        foreach (var nameId in nameIds)
        {
            if (_face.TryGetName(nameId, out var name))
            {
                return name;
            }
        }

        return null;
    }

    private void ParseFontAttributes()
    {
        if (TryGetTable("OS/2", out var os2))
        {
            var span = os2.Span;
            if (span.Length >= 8)
            {
                WeightClass = ReadUShort(span, 4);
                WidthClass = ReadUShort(span, 6);
            }

            if (span.Length >= 64)
            {
                var selection = ReadUShort(span, 62);
                IsItalic = (selection & 0x0001) != 0 || (selection & 0x0200) != 0;
            }
        }

        if (!IsItalic && TryGetTable("head", out var head) && head.Length >= 46)
        {
            IsItalic = (ReadUShort(head.Span, 44) & 0x0002) != 0;
        }
    }

    private void ParseHeadTable()
    {
        uint headOffset = _tables["head"].offset;
        UnitsPerEm = ReadUShort(headOffset + 18);
        _indexToLocFormat = ReadShort(headOffset + 50);
    }

    private void ParseHheaTable()
    {
        if (!_tables.TryGetValue("hhea", out var hhea)) return;
        uint offset = hhea.offset;
        Ascender = ReadShort(offset + 4);
        Descender = ReadShort(offset + 6);
        LineGap = ReadShort(offset + 8);
        _numberOfHMetrics = ReadUShort(offset + 34);

        if (_tables.TryGetValue("hmtx", out var hmtx))
        {
            _hmtxOffset = hmtx.offset;
        }
    }

    private void ParseMaxpTable()
    {
        if (!_tables.TryGetValue("maxp", out var maxp)) return;
        NumGlyphs = ReadUShort(maxp.offset + 4);
    }

    public ushort GetGlyphIndex(char c)
    {
        return GetGlyphIndex((uint)c);
    }

    public ushort GetGlyphIndex(uint codePoint)
    {
        return _face.TryGetGlyphIndex(codePoint, out var glyphIndex) ? glyphIndex : (ushort)0;
    }

    public bool HasGlyph(uint codePoint)
    {
        return GetGlyphIndex(codePoint) != 0;
    }

    public bool TryGetTable(string tag, out ReadOnlyMemory<byte> table)
    {
        return _face.TryGetTable(tag, out table);
    }

    public static int GetFaceCount(byte[] fontData)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        return SfntFontFace.LoadFaces(fontData).Count;
    }

    public bool TryGetBitmapGlyph(ushort glyphIndex, float targetPixelsPerEm, out BitmapGlyphData glyph)
    {
        glyph = default;
        if (glyphIndex >= NumGlyphs || !TryGetTable("sbix", out var sbixMemory))
        {
            return false;
        }

        var sbix = sbixMemory.Span;
        if (sbix.Length < 12 || ReadUShort(sbix, 0) != 1)
        {
            return false;
        }

        var strikeCount = ReadUInt(sbix, 4);
        if (strikeCount == 0 || strikeCount > int.MaxValue ||
            8L + strikeCount * 4L > sbix.Length)
        {
            return false;
        }

        var bestDistance = float.MaxValue;
        BitmapGlyphData best = default;
        var found = false;
        for (var strikeIndex = 0; strikeIndex < (int)strikeCount; strikeIndex++)
        {
            var strikeOffset = ReadUInt(sbix, 8 + strikeIndex * 4);
            var strikeEnd = strikeIndex + 1 < strikeCount
                ? ReadUInt(sbix, 8 + (strikeIndex + 1) * 4)
                : (uint)sbix.Length;
            if (!TryGetBitmapGlyphFromStrike(
                    sbixMemory,
                    strikeOffset,
                    strikeEnd,
                    glyphIndex,
                    out var candidate))
            {
                continue;
            }

            var distance = MathF.Abs(candidate.PixelsPerEm - targetPixelsPerEm);
            if (!found || distance < bestDistance ||
                (distance == bestDistance && candidate.PixelsPerEm > best.PixelsPerEm))
            {
                found = true;
                bestDistance = distance;
                best = candidate;
            }
        }

        glyph = best;
        return found;
    }

    private bool TryGetBitmapGlyphFromStrike(
        ReadOnlyMemory<byte> sbixMemory,
        uint strikeOffset,
        uint strikeEnd,
        ushort glyphIndex,
        out BitmapGlyphData glyph)
    {
        glyph = default;
        var sbix = sbixMemory.Span;
        var offsetTableLength = ((long)NumGlyphs + 1) * 4;
        if (strikeOffset > int.MaxValue || strikeEnd > sbix.Length || strikeOffset >= strikeEnd ||
            strikeOffset + 4L + offsetTableLength > strikeEnd)
        {
            return false;
        }

        var strike = (int)strikeOffset;
        var pixelsPerEm = ReadUShort(sbix, strike);
        var pixelsPerInch = ReadUShort(sbix, strike + 2);
        return TryResolveBitmapGlyph(
            sbixMemory,
            strike,
            (int)strikeEnd,
            pixelsPerEm,
            pixelsPerInch,
            glyphIndex,
            glyphIndex,
            0,
            out glyph);
    }

    private bool TryResolveBitmapGlyph(
        ReadOnlyMemory<byte> sbixMemory,
        int strikeOffset,
        int strikeEnd,
        ushort pixelsPerEm,
        ushort pixelsPerInch,
        ushort glyphIndex,
        ushort originalGlyphIndex,
        int depth,
        out BitmapGlyphData glyph)
    {
        glyph = default;
        if (depth > 16 || glyphIndex >= NumGlyphs)
        {
            return false;
        }

        var sbix = sbixMemory.Span;
        var offsets = strikeOffset + 4;
        var startRelative = ReadUInt(sbix, offsets + glyphIndex * 4);
        var endRelative = ReadUInt(sbix, offsets + (glyphIndex + 1) * 4);
        if (startRelative >= endRelative || startRelative > int.MaxValue || endRelative > int.MaxValue)
        {
            return false;
        }

        var start = strikeOffset + (int)startRelative;
        var end = strikeOffset + (int)endRelative;
        if (start < offsets || end > strikeEnd || end - start < 8)
        {
            return false;
        }

        var originOffsetX = ReadShort(sbix, start);
        var originOffsetY = ReadShort(sbix, start + 2);
        var graphicType = ReadUInt(sbix, start + 4);
        if (graphicType == 0x64757065) // "dupe"
        {
            if (end - start < 10)
            {
                return false;
            }

            var duplicateGlyphIndex = ReadUShort(sbix, start + 8);
            if (duplicateGlyphIndex == originalGlyphIndex ||
                !TryResolveBitmapGlyph(
                    sbixMemory,
                    strikeOffset,
                    strikeEnd,
                    pixelsPerEm,
                    pixelsPerInch,
                    duplicateGlyphIndex,
                    originalGlyphIndex,
                    depth + 1,
                    out var duplicate))
            {
                return false;
            }

            glyph = new BitmapGlyphData(
                pixelsPerEm,
                pixelsPerInch,
                originOffsetX,
                originOffsetY,
                duplicate.GraphicType,
                duplicate.Data);
            return true;
        }

        if (graphicType != PngBitmapGraphicType &&
            graphicType != JpegBitmapGraphicType &&
            graphicType != TiffBitmapGraphicType)
        {
            return false;
        }

        glyph = new BitmapGlyphData(
            pixelsPerEm,
            pixelsPerInch,
            originOffsetX,
            originOffsetY,
            graphicType,
            sbixMemory.Slice(start + 8, end - start - 8));
        return true;
    }

    public float GetAdvanceWidth(ushort glyphIndex, float emSize)
    {
        if (_hmtxOffset == 0 || _numberOfHMetrics == 0) return emSize * 0.5f;

        uint offset;
        if (glyphIndex < _numberOfHMetrics)
        {
            offset = _hmtxOffset + (uint)(glyphIndex * 4);
        }
        else
        {
            offset = _hmtxOffset + (uint)((_numberOfHMetrics - 1) * 4);
        }

        ushort advanceWidth = ReadUShort(offset);
        float scale = emSize / UnitsPerEm;
        return advanceWidth * scale;
    }

    public float GetKerning(char left, char right, float emSize)
    {
        return GetKerning((uint)left, (uint)right, emSize);
    }

    public float GetKerning(uint left, uint right, float emSize)
    {
        if (!_tables.TryGetValue("kern", out var kern)) return 0;
        
        uint offset = kern.offset;
        ushort version = ReadUShort(offset);
        ushort nTables = ReadUShort(offset + 2);
        
        uint subtableOffset = offset + 4;
        float scale = emSize / UnitsPerEm;

        ushort leftIdx = GetGlyphIndex(left);
        ushort rightIdx = GetGlyphIndex(right);

        for (int i = 0; i < nTables; i++)
        {
            ushort length = ReadUShort(subtableOffset + 2);
            ushort coverage = ReadUShort(subtableOffset + 4);

            // Subtable Format 0 (sorted list of kerning pairs)
            if ((coverage >> 8) == 0 && (coverage & 1) != 0)
            {
                ushort nPairs = ReadUShort(subtableOffset + 6);
                uint pairsOffset = subtableOffset + 14;

                // Perform binary search for the glyph pair
                uint key = ((uint)leftIdx << 16) | rightIdx;
                int low = 0;
                int high = nPairs - 1;

                while (low <= high)
                {
                    int mid = (low + high) / 2;
                    uint midOffset = pairsOffset + (uint)(mid * 6);
                    uint pairKey = ReadUInt(midOffset);

                    if (pairKey == key)
                    {
                        short value = ReadShort(midOffset + 4);
                        return value * scale;
                    }
                    else if (pairKey < key)
                    {
                        low = mid + 1;
                    }
                    else
                    {
                        high = mid - 1;
                    }
                }
            }
            subtableOffset += length;
        }

        return 0;
    }

    private sealed class ParsedGlyph
    {
        public ParsedGlyph(PathGeometry geometry, Vector2[] points)
        {
            Geometry = geometry;
            Points = points;
        }

        public PathGeometry Geometry { get; }
        public Vector2[] Points { get; }
    }

    private readonly Dictionary<ushort, ParsedGlyph?> _glyphOutlineCache = new();
    private readonly Dictionary<ushort, PathGeometry?> _flippedOutlineCache = new();

    public PathGeometry? GetGlyphOutline(ushort glyphIndex)
    {
        lock (_glyphOutlineCache)
        {
            if (_glyphOutlineCache.TryGetValue(glyphIndex, out var cached))
            {
                return cached?.Geometry;
            }

            var result = GetGlyphOutlineInternal(glyphIndex, new HashSet<ushort>(), 0);
            _glyphOutlineCache[glyphIndex] = result;
            return result?.Geometry;
        }
    }

    public PathGeometry? GetFlippedGlyphOutline(ushort glyphIndex)
    {
        lock (_flippedOutlineCache)
        {
            if (_flippedOutlineCache.TryGetValue(glyphIndex, out var cached))
            {
                return cached;
            }

            var rawOutline = GetGlyphOutline(glyphIndex);
            if (rawOutline == null)
            {
                _flippedOutlineCache[glyphIndex] = null;
                return null;
            }

            var flippedOutline = new PathGeometry();
            foreach (var figure in rawOutline.Figures)
            {
                var startPt = new Vector2(figure.StartPoint.X, -figure.StartPoint.Y);
                var newFigure = new PathFigure(startPt, figure.IsClosed) { IsFilled = figure.IsFilled };
                foreach (var segment in figure.Segments)
                {
                    if (segment is LineSegment line)
                    {
                        newFigure.Segments.Add(new LineSegment(new Vector2(line.Point.X, -line.Point.Y)));
                    }
                    else if (segment is QuadraticBezierSegment quad)
                    {
                        newFigure.Segments.Add(new QuadraticBezierSegment(
                            new Vector2(quad.ControlPoint.X, -quad.ControlPoint.Y),
                            new Vector2(quad.Point.X, -quad.Point.Y)
                        ));
                    }
                    else if (segment is CubicBezierSegment cubic)
                    {
                        newFigure.Segments.Add(new CubicBezierSegment(
                            new Vector2(cubic.ControlPoint1.X, -cubic.ControlPoint1.Y),
                            new Vector2(cubic.ControlPoint2.X, -cubic.ControlPoint2.Y),
                            new Vector2(cubic.Point.X, -cubic.Point.Y)
                        ));
                    }
                }
                flippedOutline.Figures.Add(newFigure);
            }

            _flippedOutlineCache[glyphIndex] = flippedOutline;
            return flippedOutline;
        }
    }

    private ParsedGlyph? GetGlyphOutlineInternal(ushort glyphIndex, HashSet<ushort> ancestors, int depth)
    {
        if (!HasTrueTypeOutlines || glyphIndex >= NumGlyphs || depth > 32 || !ancestors.Add(glyphIndex))
        {
            return null;
        }

        try
        {
            if (_glyphOutlineCache.TryGetValue(glyphIndex, out var cached))
            {
                return cached;
            }

            var result = ParseGlyphOutline(glyphIndex, ancestors, depth);
            _glyphOutlineCache[glyphIndex] = result;
            return result;
        }
        finally
        {
            ancestors.Remove(glyphIndex);
        }
    }

    private ParsedGlyph? ParseGlyphOutline(ushort glyphIndex, HashSet<ushort> ancestors, int depth)
    {
        uint startOffset = 0;
        uint endOffset = 0;

        if (_indexToLocFormat == 0) // Short offsets
        {
            startOffset = (uint)(ReadUShort(_locaOffset + (uint)(glyphIndex * 2)) * 2);
            endOffset = (uint)(ReadUShort(_locaOffset + (uint)((glyphIndex + 1) * 2)) * 2);
        }
        else // Long offsets
        {
            startOffset = ReadUInt(_locaOffset + (uint)(glyphIndex * 4));
            endOffset = ReadUInt(_locaOffset + (uint)((glyphIndex + 1) * 4));
        }

        if (startOffset == endOffset)
        {
            return null; // Empty glyph (e.g. space)
        }

        uint glyphOffset = _glyfOffset + startOffset;
        short numberOfContours = ReadShort(glyphOffset);

        if (numberOfContours < 0)
        {
            return ParseCompositeGlyphOutline(glyphOffset, ancestors, depth);
        }

        if (numberOfContours == 0)
        {
            return null;
        }

        var geometry = new PathGeometry();
        uint offset = glyphOffset + 10;

        ushort[] endPtsOfContours = new ushort[numberOfContours];
        for (int i = 0; i < numberOfContours; i++)
        {
            endPtsOfContours[i] = ReadUShort(offset);
            offset += 2;
        }

        ushort instructionLength = ReadUShort(offset);
        offset += (uint)(2 + instructionLength); // Skip instructions

        int totalPoints = endPtsOfContours[numberOfContours - 1] + 1;
        byte[] flags = new byte[totalPoints];
        
        // Read Flags
        for (int i = 0; i < totalPoints; i++)
        {
            byte flag = _data[offset++];
            flags[i] = flag;
            
            // Check if flag repeats
            if ((flag & 8) != 0)
            {
                byte repeatCount = _data[offset++];
                for (int r = 0; r < repeatCount; r++)
                {
                    flags[++i] = flag;
                }
            }
        }

        Vector2[] coords = new Vector2[totalPoints];
        
        // Read X Coordinates
        float lastX = 0;
        for (int i = 0; i < totalPoints; i++)
        {
            byte flag = flags[i];
            float xValue = 0;

            if ((flag & 2) != 0) // X Short Vector
            {
                byte val = _data[offset++];
                xValue = ((flag & 16) != 0) ? val : -val;
            }
            else
            {
                if ((flag & 16) != 0) // X Is Same
                {
                    xValue = 0;
                }
                else
                {
                    xValue = ReadShort(offset);
                    offset += 2;
                }
            }
            lastX += xValue;
            coords[i].X = lastX;
        }

        // Read Y Coordinates
        float lastY = 0;
        for (int i = 0; i < totalPoints; i++)
        {
            byte flag = flags[i];
            float yValue = 0;

            if ((flag & 4) != 0) // Y Short Vector
            {
                byte val = _data[offset++];
                yValue = ((flag & 32) != 0) ? val : -val;
            }
            else
            {
                if ((flag & 32) != 0) // Y Is Same
                {
                    yValue = 0;
                }
                else
                {
                    yValue = ReadShort(offset);
                    offset += 2;
                }
            }
            lastY += yValue;
            coords[i].Y = lastY;
        }

        // Process coordinates into PathGeometry (contour by contour)
        int ptIndex = 0;
        for (int c = 0; c < numberOfContours; c++)
        {
            int endPt = endPtsOfContours[c];
            int count = endPt - ptIndex + 1;
            if (count < 2)
            {
                ptIndex = endPt + 1;
                continue;
            }

            Vector2[] contourPoints = new Vector2[count];
            byte[] contourFlags = new byte[count];

            for (int i = 0; i < count; i++)
            {
                contourPoints[i] = coords[ptIndex + i];
                contourFlags[i] = flags[ptIndex + i];
            }

            ptIndex = endPt + 1;

            // Generate PathFigure
            PathFigure figure = DecodeContourToFigure(contourPoints, contourFlags);
            geometry.Figures.Add(figure);
        }

        return new ParsedGlyph(geometry, coords);
    }

    private ParsedGlyph? ParseCompositeGlyphOutline(uint glyphOffset, HashSet<ushort> ancestors, int depth)
    {
        const ushort ArgumentsAreWords = 0x0001;
        const ushort ArgumentsAreXyValues = 0x0002;
        const ushort RoundXyToGrid = 0x0004;
        const ushort WeHaveScale = 0x0008;
        const ushort MoreComponents = 0x0020;
        const ushort WeHaveXAndYScale = 0x0040;
        const ushort WeHaveTwoByTwo = 0x0080;
        const ushort ScaledComponentOffset = 0x0800;

        var geometry = new PathGeometry();
        var points = new List<Vector2>();
        var offset = glyphOffset + 10;
        ushort flags;

        do
        {
            if (offset + 4 > _data.Length)
            {
                return null;
            }

            flags = ReadUShort(offset);
            var componentGlyphIndex = ReadUShort(offset + 2);
            offset += 4;

            int argument1;
            int argument2;
            if ((flags & ArgumentsAreWords) != 0)
            {
                if (offset + 4 > _data.Length)
                {
                    return null;
                }

                if ((flags & ArgumentsAreXyValues) != 0)
                {
                    argument1 = ReadShort(offset);
                    argument2 = ReadShort(offset + 2);
                }
                else
                {
                    argument1 = ReadUShort(offset);
                    argument2 = ReadUShort(offset + 2);
                }
                offset += 4;
            }
            else
            {
                if (offset + 2 > _data.Length)
                {
                    return null;
                }

                if ((flags & ArgumentsAreXyValues) != 0)
                {
                    argument1 = unchecked((sbyte)_data[offset]);
                    argument2 = unchecked((sbyte)_data[offset + 1]);
                }
                else
                {
                    argument1 = _data[offset];
                    argument2 = _data[offset + 1];
                }
                offset += 2;
            }

            var m00 = 1f;
            var m01 = 0f;
            var m10 = 0f;
            var m11 = 1f;
            if ((flags & WeHaveScale) != 0)
            {
                if (offset + 2 > _data.Length)
                {
                    return null;
                }

                m00 = m11 = ReadF2Dot14(offset);
                offset += 2;
            }
            else if ((flags & WeHaveXAndYScale) != 0)
            {
                if (offset + 4 > _data.Length)
                {
                    return null;
                }

                m00 = ReadF2Dot14(offset);
                m11 = ReadF2Dot14(offset + 2);
                offset += 4;
            }
            else if ((flags & WeHaveTwoByTwo) != 0)
            {
                if (offset + 8 > _data.Length)
                {
                    return null;
                }

                m00 = ReadF2Dot14(offset);
                m01 = ReadF2Dot14(offset + 2);
                m10 = ReadF2Dot14(offset + 4);
                m11 = ReadF2Dot14(offset + 6);
                offset += 8;
            }

            var component = GetGlyphOutlineInternal(componentGlyphIndex, ancestors, depth + 1);
            if (component == null)
            {
                continue;
            }

            var linearTransform = new Matrix4x4(
                m00, m10, 0, 0,
                m01, m11, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1);

            Vector2 translation;
            if ((flags & ArgumentsAreXyValues) != 0)
            {
                translation = new Vector2(argument1, argument2);
                if ((flags & ScaledComponentOffset) != 0)
                {
                    translation = Vector2.TransformNormal(translation, linearTransform);
                }
            }
            else
            {
                if ((uint)argument1 >= (uint)points.Count ||
                    (uint)argument2 >= (uint)component.Points.Length)
                {
                    continue;
                }

                var componentPoint = Vector2.Transform(component.Points[argument2], linearTransform);
                translation = points[argument1] - componentPoint;
            }

            if ((flags & RoundXyToGrid) != 0)
            {
                translation = new Vector2(MathF.Round(translation.X), MathF.Round(translation.Y));
            }

            linearTransform.M41 = translation.X;
            linearTransform.M42 = translation.Y;
            var transformed = component.Geometry.CreateTransformed(linearTransform);
            geometry.Figures.AddRange(transformed.Figures);
            foreach (var point in component.Points)
            {
                points.Add(Vector2.Transform(point, linearTransform));
            }
        }
        while ((flags & MoreComponents) != 0);

        return geometry.Figures.Count == 0
            ? null
            : new ParsedGlyph(geometry, points.ToArray());
    }

    private float ReadF2Dot14(uint offset)
    {
        return ReadShort(offset) / 16384f;
    }

    private PathFigure DecodeContourToFigure(Vector2[] pts, byte[] flags)
    {
        var figure = new PathFigure();
        int count = pts.Length;

        // Check on-curve flags
        bool IsOnCurve(int idx) => (flags[idx] & 1) != 0;

        // Find starting point on contour
        int startIdx = 0;
        Vector2 startPoint;

        if (IsOnCurve(0))
        {
            startPoint = pts[0];
            startIdx = 1;
        }
        else if (IsOnCurve(count - 1))
        {
            startPoint = pts[count - 1];
            startIdx = 0;
        }
        else
        {
            // Both start and end are off-curve (implicit start point is halfway)
            startPoint = (pts[0] + pts[count - 1]) / 2f;
            startIdx = 0;
        }

        figure.StartPoint = startPoint;
        Vector2 current = startPoint;

        int idx = startIdx;
        int processed = 0;

        while (processed < count)
        {
            int i = idx % count;
            int iNext = (idx + 1) % count;

            Vector2 pt = pts[i];
            bool isOn = IsOnCurve(i);

            if (isOn)
            {
                figure.Segments.Add(new LineSegment(pt));
                current = pt;
                idx++;
                processed++;
            }
            else
            {
                // Quadratic Bezier control point
                Vector2 ctrl = pt;
                Vector2 end;

                if (IsOnCurve(iNext))
                {
                    end = pts[iNext];
                    idx += 2;
                    processed += 2;
                }
                else
                {
                    // Implicit on-curve end point is halfway to next off-curve point
                    end = (ctrl + pts[iNext]) / 2f;
                    idx += 1;
                    processed += 1;
                }

                figure.Segments.Add(new QuadraticBezierSegment(ctrl, end));
                current = end;
            }
        }

        figure.IsClosed = true;
        figure.IsFilled = true;
        return figure;
    }

    private void ParseColrTable()
    {
        if (!_tables.TryGetValue("COLR", out var colr)) return;
        _colrOffset = colr.offset;
        
        ushort version = ReadUShort(_colrOffset);
        if (version == 0)
        {
            _numBaseGlyphRecords = ReadUShort(_colrOffset + 2);
            _baseGlyphRecordsOffset = _colrOffset + ReadUInt(_colrOffset + 4);
            _layerRecordsOffset = _colrOffset + ReadUInt(_colrOffset + 8);
            _numLayerRecords = ReadUShort(_colrOffset + 12);
        }
    }

    private void ParseCpalTable()
    {
        if (!_tables.TryGetValue("CPAL", out var cpal)) return;
        _cpalOffset = cpal.offset;

        ushort version = ReadUShort(_cpalOffset);
        if (version == 0)
        {
            _numPaletteEntries = ReadUShort(_cpalOffset + 2);
            _numPalettes = ReadUShort(_cpalOffset + 4);
            _numColorRecords = ReadUShort(_cpalOffset + 6);
            _colorRecordsOffset = _cpalOffset + ReadUInt(_cpalOffset + 8);

            // Parse default palette (palette 0)
            _colorPalette = new Vector4[_numPaletteEntries];
            
            // Check first palette record index
            ushort firstPaletteRecordIndex = ReadUShort(_cpalOffset + 12);

            for (int i = 0; i < _numPaletteEntries; i++)
            {
                uint recordOffset = _colorRecordsOffset + (uint)((firstPaletteRecordIndex + i) * 4);
                byte b = _data[recordOffset];
                byte g = _data[recordOffset + 1];
                byte r = _data[recordOffset + 2];
                byte a = _data[recordOffset + 3];

                _colorPalette[i] = new Vector4(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f);
            }
        }
    }

    public bool HasColorLayers(ushort glyphId)
    {
        if (_numBaseGlyphRecords == 0) return false;
        
        int low = 0;
        int high = _numBaseGlyphRecords - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            uint recordOffset = _baseGlyphRecordsOffset + (uint)(mid * 6);
            ushort gid = ReadUShort(recordOffset);

            if (gid == glyphId)
            {
                return true;
            }
            else if (gid < glyphId)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        return false;
    }

    public List<FontColorLayer>? GetColorLayers(ushort glyphId)
    {
        if (_numBaseGlyphRecords == 0) return null;

        int low = 0;
        int high = _numBaseGlyphRecords - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            uint recordOffset = _baseGlyphRecordsOffset + (uint)(mid * 6);
            ushort gid = ReadUShort(recordOffset);

            if (gid == glyphId)
            {
                ushort firstLayerIndex = ReadUShort(recordOffset + 2);
                ushort numLayers = ReadUShort(recordOffset + 4);

                var layers = new List<FontColorLayer>(numLayers);
                for (int i = 0; i < numLayers; i++)
                {
                    uint layerOffset = _layerRecordsOffset + (uint)((firstLayerIndex + i) * 4);
                    ushort layerGid = ReadUShort(layerOffset);
                    ushort paletteIndex = ReadUShort(layerOffset + 2);

                    Vector4 color = Vector4.One; // Default color
                    if (paletteIndex < _numPaletteEntries && _colorPalette != null)
                    {
                        color = _colorPalette[paletteIndex];
                    }

                    layers.Add(new FontColorLayer { GlyphId = layerGid, Color = color });
                }
                return layers;
            }
            else if (gid < glyphId)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        return null;
    }

    public (GpuGlyphRecord[] Records, GpuSegment[] Segments) CompileGpuOutlineData()
    {
        var records = new GpuGlyphRecord[NumGlyphs];
        var segments = new List<GpuSegment>();

        for (ushort glyphId = 0; glyphId < NumGlyphs; glyphId++)
        {
            var outline = GetGlyphOutline(glyphId);

            uint startSegment = (uint)segments.Count;
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            if (outline != null)
            {
                foreach (var figure in outline.Figures)
                {
                    if (figure.Segments.Count == 0) continue;

                    Vector2 currentPoint = figure.StartPoint;

                    foreach (var segment in figure.Segments)
                    {
                        if (segment is LineSegment line)
                        {
                            var seg = new GpuSegment
                            {
                                P0 = currentPoint,
                                P1 = line.Point,
                                P2 = Vector2.Zero,
                                SegmentType = 0,
                                Pad = 0
                            };
                            segments.Add(seg);

                            UpdateBoundingBoxWithLine(seg.P0, seg.P1, ref minX, ref minY, ref maxX, ref maxY);
                            currentPoint = line.Point;
                        }
                        else if (segment is QuadraticBezierSegment quad)
                        {
                            var seg = new GpuSegment
                            {
                                P0 = currentPoint,
                                P1 = quad.ControlPoint,
                                P2 = quad.Point,
                                SegmentType = 1,
                                Pad = 0
                            };
                            segments.Add(seg);

                            UpdateBoundingBoxWithQuad(seg.P0, seg.P1, seg.P2, ref minX, ref minY, ref maxX, ref maxY);
                            currentPoint = quad.Point;
                        }
                    }

                    if (figure.IsClosed && currentPoint != figure.StartPoint)
                    {
                        var seg = new GpuSegment
                        {
                            P0 = currentPoint,
                            P1 = figure.StartPoint,
                            P2 = Vector2.Zero,
                            SegmentType = 0,
                            Pad = 0
                        };
                        segments.Add(seg);

                        UpdateBoundingBoxWithLine(seg.P0, seg.P1, ref minX, ref minY, ref maxX, ref maxY);
                        currentPoint = figure.StartPoint;
                    }
                }
            }

            uint segmentCount = (uint)segments.Count - startSegment;

            if (segmentCount > 0)
            {
                records[glyphId] = new GpuGlyphRecord
                {
                    StartSegment = startSegment,
                    SegmentCount = segmentCount,
                    MinX = minX,
                    MinY = minY,
                    MaxX = maxX,
                    MaxY = maxY,
                    Pad0 = 0,
                    Pad1 = 0
                };
            }
            else
            {
                records[glyphId] = new GpuGlyphRecord
                {
                    StartSegment = 0,
                    SegmentCount = 0,
                    MinX = 0,
                    MinY = 0,
                    MaxX = 0,
                    MaxY = 0,
                    Pad0 = 0,
                    Pad1 = 0
                };
            }
        }

        return (records, segments.ToArray());
    }

    private static void UpdateBoundingBoxWithLine(Vector2 p0, Vector2 p1, ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        UpdateMinMax(p0, ref minX, ref minY, ref maxX, ref maxY);
        UpdateMinMax(p1, ref minX, ref minY, ref maxX, ref maxY);
    }

    private static void UpdateBoundingBoxWithQuad(Vector2 p0, Vector2 p1, Vector2 p2, ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        UpdateMinMax(p0, ref minX, ref minY, ref maxX, ref maxY);
        UpdateMinMax(p2, ref minX, ref minY, ref maxX, ref maxY);

        // X extremum
        float denomX = p0.X - 2 * p1.X + p2.X;
        if (Math.Abs(denomX) > 1e-6f)
        {
            float t = (p0.X - p1.X) / denomX;
            if (t > 0 && t < 1)
            {
                float x = (1 - t) * (1 - t) * p0.X + 2 * (1 - t) * t * p1.X + t * t * p2.X;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
            }
        }

        // Y extremum
        float denomY = p0.Y - 2 * p1.Y + p2.Y;
        if (Math.Abs(denomY) > 1e-6f)
        {
            float t = (p0.Y - p1.Y) / denomY;
            if (t > 0 && t < 1)
            {
                float y = (1 - t) * (1 - t) * p0.Y + 2 * (1 - t) * t * p1.Y + t * t * p2.Y;
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }
    }

    private static void UpdateMinMax(Vector2 p, ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        if (p.X < minX) minX = p.X;
        if (p.X > maxX) maxX = p.X;
        if (p.Y < minY) minY = p.Y;
        if (p.Y > maxY) maxY = p.Y;
    }
}

public struct FontColorLayer
{
    public ushort GlyphId;
    public Vector4 Color;
}
