using System.IO.Compression;
using System.Security.Cryptography;
using KingdomRush.ModPatcher.Core.Errors;

namespace KingdomRush.ModPatcher.Core.Transactions;

public sealed record SaveBackupResult(string Path, string Sha256, IReadOnlyList<string> Files);

public static class SaveBackupService
{
    public static SaveBackupResult Create(string saveDirectory, string destination)
    {
        string[] files = Directory.GetFiles(saveDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(saveDirectory, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        using (ZipArchive archive = ZipFile.Open(destination, ZipArchiveMode.Create))
        {
            foreach (string relative in files)
            {
                string source = Path.Combine(saveDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
                archive.CreateEntryFromFile(source, relative, CompressionLevel.Optimal);
            }
        }
        Verify(destination, files);
        return new SaveBackupResult(destination, HashFile(destination), files);
    }

    public static void Verify(string path, IReadOnlyList<string> expectedFiles)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(path);
            string[] names = archive.Entries.Select(entry => entry.FullName).ToArray();
            if (!names.SequenceEqual(expectedFiles, StringComparer.Ordinal))
            {
                throw new PatcherException("Save backup file list differs from its manifest.");
            }
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                using Stream source = entry.Open();
                source.CopyTo(Stream.Null);
            }
        }
        catch (InvalidDataException error)
        {
            throw new PatcherException("Save backup is not a valid ZIP archive.", error);
        }
    }

    public static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
