using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ProGPU.Vector;

namespace ProGPU.Text;

public class TtfFont
{
    private readonly byte[] _data;
    private readonly Dictionary<string, (uint offset, uint length)> _tables = new();

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

    // Cmap format 4 variables
    private uint _cmapOffset;
    private ushort _segCount;
    private ushort[] _endCodes = null!;
    private ushort[] _startCodes = null!;
    private short[] _idDeltas = null!;
    private ushort[] _idRangeOffsets = null!;
    private uint _idRangeOffsetsTableOffset;

    // Cmap format 12 variables
    private uint _cmap12Offset;
    private uint _numGroups12;
    private uint[] _startCharCodes12 = null!;
    private uint[] _endCharCodes12 = null!;
    private uint[] _startGlyphIds12 = null!;

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
    {
        _data = fontData;
        ParseTableDirectory();
        ParseHeadTable();
        ParseHheaTable();
        ParseMaxpTable();
        ParseCmapTable();
        ParseColrTable();
        ParseCpalTable();
    }

    public TtfFont(string filePath) : this(File.ReadAllBytes(filePath))
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
    #endregion

    private void ParseTableDirectory()
    {
        uint numTables = ReadUShort(4);
        uint offset = 12;

        for (int i = 0; i < numTables; i++)
        {
            char c0 = (char)_data[offset];
            char c1 = (char)_data[offset + 1];
            char c2 = (char)_data[offset + 2];
            char c3 = (char)_data[offset + 3];
            string tag = $"{c0}{c1}{c2}{c3}";

            uint checksum = ReadUInt(offset + 4);
            uint tableOffset = ReadUInt(offset + 8);
            uint length = ReadUInt(offset + 12);

            _tables[tag] = (tableOffset, length);
            offset += 16;
        }

        if (!_tables.ContainsKey("head") || !_tables.ContainsKey("cmap") || !_tables.ContainsKey("glyf") || !_tables.ContainsKey("loca"))
        {
            throw new FormatException("Font file is missing essential TTF tables (head, cmap, glyf, or loca).");
        }

        _locaOffset = _tables["loca"].offset;
        _glyfOffset = _tables["glyf"].offset;
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

    private void ParseCmapTable()
    {
        uint cmapTableOffset = _tables["cmap"].offset;
        ushort version = ReadUShort(cmapTableOffset);
        ushort numTables = ReadUShort(cmapTableOffset + 2);

        uint subtable4Offset = 0;
        uint subtable12Offset = 0;

        for (int i = 0; i < numTables; i++)
        {
            uint recordOffset = cmapTableOffset + 4 + (uint)(i * 8);
            ushort platformId = ReadUShort(recordOffset);
            ushort encodingId = ReadUShort(recordOffset + 2);
            uint offset = ReadUInt(recordOffset + 4);

            // Format 12 is Platform 3 Encoding 10 (Unicode Full) or Platform 0 Encoding 4 (Unicode Full)
            if ((platformId == 3 && encodingId == 10) || (platformId == 0 && encodingId == 4))
            {
                subtable12Offset = cmapTableOffset + offset;
            }
            // Format 4 is Platform 3 Encoding 1 (Unicode BMP) or Platform 0
            else if ((platformId == 3 && encodingId == 1) || (platformId == 0))
            {
                if (subtable4Offset == 0) subtable4Offset = cmapTableOffset + offset;
            }
        }

        // If we found a format 12 subtable, parse it
        if (subtable12Offset != 0)
        {
            ushort format = ReadUShort(subtable12Offset);
            if (format == 12)
            {
                _cmap12Offset = subtable12Offset;
                _numGroups12 = ReadUInt(_cmap12Offset + 12);
                _startCharCodes12 = new uint[_numGroups12];
                _endCharCodes12 = new uint[_numGroups12];
                _startGlyphIds12 = new uint[_numGroups12];

                uint groupOffset = _cmap12Offset + 16;
                for (uint i = 0; i < _numGroups12; i++)
                {
                    _startCharCodes12[i] = ReadUInt(groupOffset + i * 12);
                    _endCharCodes12[i] = ReadUInt(groupOffset + i * 12 + 4);
                    _startGlyphIds12[i] = ReadUInt(groupOffset + i * 12 + 8);
                }
            }
        }

        // We MUST always have a fallback format 4 table for compatibility and core characters
        uint subtableOffset = subtable4Offset;
        if (subtableOffset == 0)
        {
            if (subtable12Offset != 0) return; // format 12 is active
            throw new NotSupportedException("Could not find a supported Unicode cmap subtable in TTF font.");
        }

        ushort format4 = ReadUShort(subtableOffset);
        if (format4 != 4)
        {
            if (subtable12Offset != 0) return; // format 12 is active
            throw new NotSupportedException($"Only TTF Cmap Format 4 or 12 is supported. Found format {format4}.");
        }

        _cmapOffset = subtableOffset;
        _segCount = (ushort)(ReadUShort(_cmapOffset + 6) / 2);

        _endCodes = new ushort[_segCount];
        _startCodes = new ushort[_segCount];
        _idDeltas = new short[_segCount];
        _idRangeOffsets = new ushort[_segCount];

        uint endCodeOffset = _cmapOffset + 14;
        uint startCodeOffset = endCodeOffset + (uint)(_segCount * 2) + 2;
        uint idDeltaOffset = startCodeOffset + (uint)(_segCount * 2);
        uint idRangeOffsetOffset = idDeltaOffset + (uint)(_segCount * 2);
        _idRangeOffsetsTableOffset = idRangeOffsetOffset;

        for (int i = 0; i < _segCount; i++)
        {
            _endCodes[i] = ReadUShort(endCodeOffset + (uint)(i * 2));
            _startCodes[i] = ReadUShort(startCodeOffset + (uint)(i * 2));
            _idDeltas[i] = ReadShort(idDeltaOffset + (uint)(i * 2));
            _idRangeOffsets[i] = ReadUShort(idRangeOffsetOffset + (uint)(i * 2));
        }
    }

    public ushort GetGlyphIndex(char c)
    {
        return GetGlyphIndex((uint)c);
    }

    public ushort GetGlyphIndex(uint codePoint)
    {
        // 1. Try to search Format 12 subtable first
        if (_numGroups12 > 0)
        {
            int low = 0;
            int high = (int)_numGroups12 - 1;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                uint start = _startCharCodes12[mid];
                uint end = _endCharCodes12[mid];
                if (codePoint >= start && codePoint <= end)
                {
                    return (ushort)(_startGlyphIds12[mid] + (codePoint - start));
                }
                else if (codePoint < start)
                {
                    high = mid - 1;
                }
                else
                {
                    low = mid + 1;
                }
            }
        }

        // 2. Fall back to Format 4 subtable
        if (_segCount > 0 && codePoint <= 0xFFFF)
        {
            ushort code = (ushort)codePoint;
            int segment = -1;

            for (int i = 0; i < _segCount; i++)
            {
                if (_endCodes[i] >= code)
                {
                    segment = i;
                    break;
                }
            }

            if (segment == -1 || _startCodes[segment] > code)
            {
                return 0; // Missing glyph (usually rectangle index 0)
            }

            ushort rangeOffset = _idRangeOffsets[segment];
            if (rangeOffset == 0)
            {
                return (ushort)((code + _idDeltas[segment]) & 0xFFFF);
            }

            // Complex range offset lookup in TTF format 4
            uint rangeOffsetAddress = _idRangeOffsetsTableOffset + (uint)(segment * 2);
            uint glyphIndexAddress = rangeOffsetAddress + rangeOffset + (uint)((code - _startCodes[segment]) * 2);
            
            ushort rawIndex = ReadUShort(glyphIndexAddress);
            if (rawIndex != 0)
            {
                return (ushort)((rawIndex + _idDeltas[segment]) & 0xFFFF);
            }
        }

        return 0;
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

    public PathGeometry? GetGlyphOutline(ushort glyphIndex)
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

        if (numberOfContours <= 0)
        {
            // Composite glyphs or complex formats are simplified or skipped in simple core
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

        return geometry;
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
}

public struct FontColorLayer
{
    public ushort GlyphId;
    public Vector4 Color;
}
