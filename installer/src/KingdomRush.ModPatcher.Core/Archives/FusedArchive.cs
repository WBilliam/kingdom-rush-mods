using System.IO.Compression;
using KingdomRush.ModPatcher.Core.Errors;

namespace KingdomRush.ModPatcher.Core.Archives;

public sealed record FusedArchiveEntry(
    string Name,
    byte[] Data,
    DateTimeOffset LastWriteTime,
    int ExternalAttributes);

public sealed class FusedArchive
{
    private FusedArchive(byte[] prefix, IReadOnlyList<FusedArchiveEntry> entries)
    {
        Prefix = prefix;
        Entries = entries;
    }

    public byte[] Prefix { get; }
    public IReadOnlyList<FusedArchiveEntry> Entries { get; }

    public static FusedArchive Read(ReadOnlyMemory<byte> image)
    {
        ZipLayout layout = ZipLayout.Parse(image);
        if (layout.HasEncryptedEntries)
        {
            throw new CompatibilityException("Encrypted ZIP entries are not supported.");
        }

        byte[] zipBytes = image.Slice(layout.PrefixLength).ToArray();
        List<FusedArchiveEntry> entries = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            using MemoryStream stream = new(zipBytes, writable: false);
            using ZipArchive archive = new(stream, ZipArchiveMode.Read);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                ValidateName(entry.FullName);
                if (!names.Add(entry.FullName))
                {
                    throw new CompatibilityException($"ZIP contains duplicate entry: {entry.FullName}");
                }

                using Stream source = entry.Open();
                using MemoryStream output = new();
                source.CopyTo(output);
                entries.Add(new FusedArchiveEntry(
                    entry.FullName,
                    output.ToArray(),
                    entry.LastWriteTime,
                    entry.ExternalAttributes));
            }
        }
        catch (CompatibilityException)
        {
            throw;
        }
        catch (InvalidDataException error)
        {
            throw new PatcherException("ZIP entry data failed integrity validation.", error);
        }

        if (!names.Contains("main.lua"))
        {
            throw new CompatibilityException("ZIP archive is missing main.lua.");
        }
        return new FusedArchive(image[..layout.PrefixLength].ToArray(), entries);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.StartsWith('/') || Path.IsPathRooted(name))
        {
            throw new CompatibilityException($"ZIP contains unsafe entry name: {name}");
        }
        string[] segments = name.Split('/');
        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new CompatibilityException($"ZIP contains unsafe entry name: {name}");
        }
    }
}
