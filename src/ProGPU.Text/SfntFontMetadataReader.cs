using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;

namespace ProGPU.Text;

internal static class SfntFontMetadataReader
{
    private const int SfntHeaderSize = 12;
    private const int TableRecordSize = 16;
    private const int NameHeaderSize = 6;
    private const int NameRecordSize = 12;
    private const int MaxFaceCount = 4096;
    private const int MaxTableCount = 4096;
    private const int MaxCharacterMapSize = 64 * 1024 * 1024;

    public static bool TryReadFontInfos(string file, out List<FontInfo> infos)
    {
        ArgumentNullException.ThrowIfNull(file);

        try
        {
            using var stream = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.RandomAccess);
            infos = ReadFontInfos(stream, file);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            infos = new List<FontInfo>();
            return false;
        }
    }

    public static bool TryReadCharacterMap(string file, int faceIndex, out byte[] characterMap)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);

        try
        {
            using var stream = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.RandomAccess);
            uint[] faceOffsets = ReadFaceOffsets(stream);
            if ((uint)faceIndex >= (uint)faceOffsets.Length)
            {
                characterMap = Array.Empty<byte>();
                return false;
            }

            return TryReadFaceTable(stream, faceOffsets[faceIndex], "cmap", out characterMap);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException or OverflowException)
        {
            characterMap = Array.Empty<byte>();
            return false;
        }
    }

    private static List<FontInfo> ReadFontInfos(Stream stream, string file)
    {
        uint[] faceOffsets = ReadFaceOffsets(stream);
        var infos = new List<FontInfo>(faceOffsets.Length);
        var fallbackName = Path.GetFileNameWithoutExtension(file);

        for (var faceIndex = 0; faceIndex < faceOffsets.Length; faceIndex++)
        {
            NameSelection names = ReadFaceNames(stream, faceOffsets[faceIndex]);
            var familyName = names.PreferredFamilyName ?? names.FamilyName ?? fallbackName;
            var fullName = names.FullName ?? familyName;
            infos.Add(new FontInfo
            {
                Name = fullName,
                FamilyName = familyName,
                FilePath = file,
                FaceIndex = faceIndex
            });
        }

        return infos;
    }

    private static uint[] ReadFaceOffsets(Stream stream)
    {
        Span<byte> header = stackalloc byte[SfntHeaderSize];
        ReadExactly(stream, 0, header);
        if (!HasTag(header, "ttcf"))
        {
            return new[] { 0u };
        }

        uint faceCountValue = ReadUInt(header, 8);
        if (faceCountValue == 0 || faceCountValue > MaxFaceCount)
        {
            throw new FormatException("TrueType collection has an invalid face count.");
        }

        var faceCount = checked((int)faceCountValue);
        var offsets = new uint[faceCount];
        Span<byte> offsetBytes = stackalloc byte[4];
        for (var i = 0; i < offsets.Length; i++)
        {
            ReadExactly(stream, SfntHeaderSize + (long)i * 4, offsetBytes);
            offsets[i] = ReadUInt(offsetBytes, 0);
        }

        return offsets;
    }

    private static NameSelection ReadFaceNames(Stream stream, uint faceOffset)
    {
        Span<byte> header = stackalloc byte[SfntHeaderSize];
        ReadExactly(stream, faceOffset, header);
        ushort tableCount = ReadUShort(header, 4);
        if (tableCount > MaxTableCount)
        {
            throw new FormatException("SFNT face has an invalid table count.");
        }

        Span<byte> record = stackalloc byte[TableRecordSize];
        for (var i = 0; i < tableCount; i++)
        {
            var recordOffset = checked((long)faceOffset + SfntHeaderSize + (long)i * TableRecordSize);
            ReadExactly(stream, recordOffset, record);
            if (!HasTag(record, "name"))
            {
                continue;
            }

            uint tableOffset = ReadUInt(record, 8);
            uint tableLength = ReadUInt(record, 12);
            return ReadNameTable(stream, tableOffset, tableLength);
        }

        return default;
    }

    private static bool TryReadFaceTable(Stream stream, uint faceOffset, string tag, out byte[] table)
    {
        Span<byte> header = stackalloc byte[SfntHeaderSize];
        ReadExactly(stream, faceOffset, header);
        ushort tableCount = ReadUShort(header, 4);
        if (tableCount > MaxTableCount)
        {
            throw new FormatException("SFNT face has an invalid table count.");
        }

        Span<byte> record = stackalloc byte[TableRecordSize];
        for (var i = 0; i < tableCount; i++)
        {
            var recordOffset = checked((long)faceOffset + SfntHeaderSize + (long)i * TableRecordSize);
            ReadExactly(stream, recordOffset, record);
            if (!HasTag(record, tag))
            {
                continue;
            }

            uint tableOffset = ReadUInt(record, 8);
            uint tableLength = ReadUInt(record, 12);
            if (tableLength == 0 || tableLength > MaxCharacterMapSize)
            {
                table = Array.Empty<byte>();
                return false;
            }

            table = GC.AllocateUninitializedArray<byte>(checked((int)tableLength));
            ReadExactly(stream, tableOffset, table);
            return true;
        }

        table = Array.Empty<byte>();
        return false;
    }

    private static NameSelection ReadNameTable(Stream stream, uint tableOffset, uint tableLength)
    {
        if (tableLength < NameHeaderSize)
        {
            return default;
        }

        var tableEnd = checked((long)tableOffset + tableLength);
        Span<byte> header = stackalloc byte[NameHeaderSize];
        ReadExactly(stream, tableOffset, header);
        ushort recordCount = ReadUShort(header, 2);
        ushort stringOffset = ReadUShort(header, 4);
        var recordsEnd = checked((long)tableOffset + NameHeaderSize + (long)recordCount * NameRecordSize);
        var stringsStart = checked((long)tableOffset + stringOffset);
        if (recordsEnd > tableEnd || stringsStart > tableEnd)
        {
            throw new FormatException("SFNT name table is truncated.");
        }

        var selection = new NameSelection();
        Span<byte> record = stackalloc byte[NameRecordSize];
        for (var i = 0; i < recordCount; i++)
        {
            ReadExactly(stream, (long)tableOffset + NameHeaderSize + (long)i * NameRecordSize, record);
            ushort nameId = ReadUShort(record, 6);
            if (nameId is not (SfntNameIds.FamilyName or SfntNameIds.FullName or SfntNameIds.PreferredFamilyName))
            {
                continue;
            }

            ushort platformId = ReadUShort(record, 0);
            ushort encodingId = ReadUShort(record, 2);
            ushort languageId = ReadUShort(record, 4);
            ushort valueLength = ReadUShort(record, 8);
            ushort valueOffset = ReadUShort(record, 10);
            if (valueLength == 0)
            {
                continue;
            }

            var absoluteValueOffset = checked(stringsStart + valueOffset);
            if (absoluteValueOffset > tableEnd || valueLength > tableEnd - absoluteValueOffset)
            {
                continue;
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(valueLength);
            try
            {
                var bytes = buffer.AsSpan(0, valueLength);
                ReadExactly(stream, absoluteValueOffset, bytes);
                var value = SfntFontFace.DecodeName(bytes, platformId, encodingId);
                var score = SfntFontFace.GetNameScore(platformId, languageId, value);
                if (!string.IsNullOrWhiteSpace(value) && selection.ShouldRead(nameId, score))
                {
                    selection.Set(nameId, value, score);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        return selection;
    }

    private static void ReadExactly(Stream stream, long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset > stream.Length || buffer.Length > stream.Length - offset)
        {
            throw new FormatException("SFNT data is truncated.");
        }

        stream.Position = offset;
        stream.ReadExactly(buffer);
    }

    private static bool HasTag(ReadOnlySpan<byte> data, string tag)
    {
        return data.Length >= 4 &&
               data[0] == tag[0] &&
               data[1] == tag[1] &&
               data[2] == tag[2] &&
               data[3] == tag[3];
    }

    private static ushort ReadUShort(ReadOnlySpan<byte> data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static uint ReadUInt(ReadOnlySpan<byte> data, int offset)
    {
        return (uint)((data[offset] << 24) |
                      (data[offset + 1] << 16) |
                      (data[offset + 2] << 8) |
                       data[offset + 3]);
    }

    private struct NameSelection
    {
        private int _preferredFamilyScore;
        private int _familyScore;
        private int _fullNameScore;

        public string? PreferredFamilyName { get; private set; }
        public string? FamilyName { get; private set; }
        public string? FullName { get; private set; }

        public readonly bool ShouldRead(ushort nameId, int score)
        {
            return nameId switch
            {
                SfntNameIds.PreferredFamilyName => PreferredFamilyName == null || score > _preferredFamilyScore,
                SfntNameIds.FamilyName => FamilyName == null || score > _familyScore,
                SfntNameIds.FullName => FullName == null || score > _fullNameScore,
                _ => false
            };
        }

        public void Set(ushort nameId, string value, int score)
        {
            switch (nameId)
            {
                case SfntNameIds.PreferredFamilyName:
                    PreferredFamilyName = value;
                    _preferredFamilyScore = score;
                    break;
                case SfntNameIds.FamilyName:
                    FamilyName = value;
                    _familyScore = score;
                    break;
                case SfntNameIds.FullName:
                    FullName = value;
                    _fullNameScore = score;
                    break;
            }
        }
    }
}
