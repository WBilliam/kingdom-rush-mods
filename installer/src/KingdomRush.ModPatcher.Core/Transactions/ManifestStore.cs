using System.Text.Json;
using KingdomRush.ModPatcher.Core.Errors;

namespace KingdomRush.ModPatcher.Core.Transactions;

public class ManifestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public virtual void WriteAtomic(string path, InstallManifest manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(payload);
                stream.WriteByte((byte)'\n');
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public virtual InstallManifest Read(string path)
    {
        try
        {
            InstallManifest manifest = JsonSerializer.Deserialize<InstallManifest>(File.ReadAllBytes(path), JsonOptions)
                ?? throw new RecoveryException($"Manifest is empty: {path}");
            if (manifest.SchemaVersion != 1)
            {
                throw new RecoveryException($"Unsupported manifest schema: {manifest.SchemaVersion}");
            }
            return manifest;
        }
        catch (RecoveryException) { throw; }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new RecoveryException($"Cannot read manifest safely: {path}", error);
        }
    }
}
