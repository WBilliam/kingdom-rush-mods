using System.Buffers.Binary;
using KingdomRush.ModPatcher.Core.Errors;

namespace KingdomRush.ModPatcher.Core.Archives;

public sealed record ZipLayout(
    int PrefixLength,
    int CentralDirectoryOffset,
    int CentralDirectorySize,
    int EntryCount,
    int EndRecordOffset,
    bool HasEncryptedEntries)
{
    private const uint EndSignature = 0x06054b50;
    private const uint CentralSignature = 0x02014b50;
    private const uint LocalSignature = 0x04034b50;
    private const int EndRecordLength = 22;
    private const int CentralFixedLength = 46;

    public static ZipLayout Parse(ReadOnlyMemory<byte> image)
    {
        ReadOnlySpan<byte> bytes = image.Span;
        int minimum = Math.Max(0, bytes.Length - (ushort.MaxValue + EndRecordLength));
        int end = -1;
        for (int index = bytes.Length - EndRecordLength; index >= minimum; index--)
        {
            if (ReadUInt32(bytes, index) != EndSignature)
            {
                continue;
            }

            int commentLength = ReadUInt16(bytes, index + 20);
            if (index + EndRecordLength + commentLength == bytes.Length)
            {
                end = index;
                break;
            }
        }

        if (end < 0)
        {
            throw new CompatibilityException("ZIP end of central directory record is missing or truncated.");
        }

        int disk = ReadUInt16(bytes, end + 4);
        int centralDisk = ReadUInt16(bytes, end + 6);
        int diskEntries = ReadUInt16(bytes, end + 8);
        int totalEntries = ReadUInt16(bytes, end + 10);
        uint centralSizeValue = ReadUInt32(bytes, end + 12);
        uint centralOffsetValue = ReadUInt32(bytes, end + 16);
        if (disk != 0 || centralDisk != 0 || diskEntries != totalEntries)
        {
            throw new CompatibilityException("Multi-disk ZIP archives are not supported.");
        }
        if (totalEntries == ushort.MaxValue || centralSizeValue == uint.MaxValue || centralOffsetValue == uint.MaxValue)
        {
            throw new CompatibilityException("ZIP64 archives are not supported.");
        }

        int centralSize = checked((int)centralSizeValue);
        int centralOffset = checked((int)centralOffsetValue);
        int prefixLength = checked(end - centralSize - centralOffset);
        if (prefixLength < 0 || prefixLength + centralOffset + centralSize != end)
        {
            throw new CompatibilityException("ZIP central directory bounds are invalid.");
        }

        int cursor = prefixLength + centralOffset;
        int centralEnd = cursor + centralSize;
        int minimumLocalOffset = int.MaxValue;
        bool encrypted = false;
        for (int entryIndex = 0; entryIndex < totalEntries; entryIndex++)
        {
            if (cursor + CentralFixedLength > centralEnd || ReadUInt32(bytes, cursor) != CentralSignature)
            {
                throw new CompatibilityException("ZIP central directory entry is malformed.");
            }

            ushort flags = ReadUInt16(bytes, cursor + 8);
            encrypted |= (flags & 1) != 0;
            int nameLength = ReadUInt16(bytes, cursor + 28);
            int extraLength = ReadUInt16(bytes, cursor + 30);
            int commentLength = ReadUInt16(bytes, cursor + 32);
            int localOffset = checked((int)ReadUInt32(bytes, cursor + 42));
            minimumLocalOffset = Math.Min(minimumLocalOffset, localOffset);
            int localAbsolute = checked(prefixLength + localOffset);
            if (localAbsolute < prefixLength || localAbsolute + 4 > bytes.Length || ReadUInt32(bytes, localAbsolute) != LocalSignature)
            {
                throw new CompatibilityException("ZIP local entry offset is invalid.");
            }
            cursor = checked(cursor + CentralFixedLength + nameLength + extraLength + commentLength);
        }

        if (cursor != centralEnd || (totalEntries > 0 && minimumLocalOffset != 0))
        {
            throw new CompatibilityException("ZIP central directory size or local offsets are invalid.");
        }

        return new ZipLayout(prefixLength, centralOffset, centralSize, totalEntries, end, encrypted);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset)
    {
        if ((uint)offset > (uint)(bytes.Length - 2))
        {
            throw new CompatibilityException("ZIP structure is truncated.");
        }
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
    {
        if ((uint)offset > (uint)(bytes.Length - 4))
        {
            return 0;
        }
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
    }
}
