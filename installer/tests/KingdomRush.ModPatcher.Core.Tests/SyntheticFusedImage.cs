using System.IO.Compression;
using System.Text;

namespace KingdomRush.ModPatcher.Core.Tests;

internal static class SyntheticFusedImage
{
    internal static readonly byte[] Prefix =
    [
        (byte)'M', (byte)'Z', 0, 1,
        (byte)'P', (byte)'K', 3, 4,
        9, 8, 7, 6, 5, 4, 3, 2, 1,
    ];

    internal static byte[] Create(params (string Name, byte[] Data)[] entries)
    {
        using MemoryStream zipBytes = new();
        using (ZipArchive archive = new(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] data) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2024, 1, 2, 3, 4, 6, TimeSpan.Zero);
                using Stream target = entry.Open();
                target.Write(data);
            }
        }

        return [.. Prefix, .. zipBytes.ToArray()];
    }

    internal static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
}
