using System.IO.Compression;
using System.Security.Cryptography;
using KingdomRush.ModPatcher.Core.Compatibility;
using KingdomRush.ModPatcher.Core.Errors;
using KingdomRush.ModPatcher.Core.Payloads;

namespace KingdomRush.ModPatcher.Core.Archives;

public static class PatchImageBuilder
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "_modpack_main_orig.lua",
        "reverse_gold.lua",
        "multi_hero.lua",
    };

    public static PatchImageResult Build(
        ReadOnlyMemory<byte> original,
        SupportedBuild build,
        ModPayloadSet payloads)
    {
        payloads.ValidateHashes();
        string originalHash = Sha256(original.Span);
        if (!HashEquals(originalHash, build.ExecutableSha256))
        {
            throw new CompatibilityException(
                $"Executable SHA-256 mismatch: expected {build.ExecutableSha256}, got {originalHash}.");
        }

        FusedArchive source = FusedArchive.Read(original);
        FusedArchiveEntry main = source.Entries.Single(entry =>
            string.Equals(entry.Name, "main.lua", StringComparison.OrdinalIgnoreCase));
        string mainHash = Sha256(main.Data);
        if (!HashEquals(mainHash, build.OriginalMainSha256))
        {
            throw new CompatibilityException(
                $"main.lua SHA-256 mismatch: expected {build.OriginalMainSha256}, got {mainHash}.");
        }

        FusedArchiveEntry? conflict = source.Entries.FirstOrDefault(entry => ReservedNames.Contains(entry.Name));
        if (conflict is not null)
        {
            throw new CompatibilityException($"Archive already contains reserved entry: {conflict.Name}");
        }

        using MemoryStream zip = new();
        using (ZipArchive target = new(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (FusedArchiveEntry entry in source.Entries)
            {
                string name = string.Equals(entry.Name, "main.lua", StringComparison.OrdinalIgnoreCase)
                    ? "_modpack_main_orig.lua"
                    : entry.Name;
                WriteEntry(target, name, entry.Data, entry.LastWriteTime, entry.ExternalAttributes);
            }
            WriteEntry(target, "main.lua", payloads.Bootstrap, main.LastWriteTime, main.ExternalAttributes);
            WriteEntry(target, "reverse_gold.lua", payloads.ReverseGold, main.LastWriteTime, main.ExternalAttributes);
            WriteEntry(target, "multi_hero.lua", payloads.FourHero, main.LastWriteTime, main.ExternalAttributes);
        }

        byte[] patched = [.. source.Prefix, .. zip.ToArray()];
        FusedArchive verified = FusedArchive.Read(patched);
        Dictionary<string, string> entryHashes = verified.Entries.ToDictionary(
            entry => entry.Name,
            entry => Sha256(entry.Data),
            StringComparer.OrdinalIgnoreCase);
        RequireExactEntry(verified, "main.lua", payloads.Bootstrap);
        RequireExactEntry(verified, "reverse_gold.lua", payloads.ReverseGold);
        RequireExactEntry(verified, "multi_hero.lua", payloads.FourHero);
        RequireExactEntry(verified, "_modpack_main_orig.lua", main.Data);
        return new PatchImageResult(patched, originalHash, Sha256(patched), entryHashes);
    }

    private static void WriteEntry(
        ZipArchive archive,
        string name,
        byte[] data,
        DateTimeOffset timestamp,
        int externalAttributes)
    {
        ZipArchiveEntry target = archive.CreateEntry(name, CompressionLevel.Optimal);
        target.LastWriteTime = timestamp;
        target.ExternalAttributes = externalAttributes;
        using Stream stream = target.Open();
        stream.Write(data);
    }

    private static void RequireExactEntry(FusedArchive archive, string name, byte[] expected)
    {
        FusedArchiveEntry entry = archive.Entries.Single(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (!entry.Data.AsSpan().SequenceEqual(expected))
        {
            throw new PatcherException($"Patched archive entry failed verification: {name}");
        }
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool HashEquals(string actualHex, string expectedHex)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualHex),
                Convert.FromHexString(expectedHex));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
