using System.Security.Cryptography;
using KingdomRush.ModPatcher.Core.Archives;
using KingdomRush.ModPatcher.Core.Compatibility;
using KingdomRush.ModPatcher.Core.Errors;
using KingdomRush.ModPatcher.Core.Payloads;

namespace KingdomRush.ModPatcher.Core.Transactions;

public sealed class ModPackService
{
    private const long SafetyMargin = 64L * 1024 * 1024;
    private const string ModPackVersion = "0.1.0";
    private const string ReverseGoldVersion = "1.0.1";
    private const string FourHeroVersion = "0.1.4";
    private readonly SupportedBuild _build;
    private readonly ModPayloadSet _payloads;
    private readonly IFileSystemCapabilities _capabilities;
    private readonly ManifestStore _manifests;
    private readonly Func<DateTimeOffset> _clock;

    public ModPackService(
        SupportedBuild build,
        ModPayloadSet payloads,
        IFileSystemCapabilities capabilities,
        ManifestStore manifests,
        Func<DateTimeOffset>? clock = null)
    {
        _build = build;
        _payloads = payloads;
        _capabilities = capabilities;
        _manifests = manifests;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<InstallManifest> InstallAsync(
        InstallPaths paths,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(paths.ManifestPath)) throw new PatcherException("The mod pack is already installed.");
        if (File.Exists(paths.PendingManifestPath))
        {
            RecoverPendingAsync(paths, cancellationToken).GetAwaiter().GetResult();
            if (File.Exists(paths.ManifestPath)) throw new PatcherException("The mod pack is already installed.");
        }
        if (!Directory.Exists(paths.SaveDirectory))
        {
            throw new PatcherException($"Save directory is missing: {paths.SaveDirectory}");
        }

        _capabilities.EnsureExclusiveWrite(paths.ExecutablePath);
        byte[] original = File.ReadAllBytes(paths.ExecutablePath);
        long saveBytes = Directory.GetFiles(paths.SaveDirectory, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
        long required = checked((long)original.Length * 2 + saveBytes + SafetyMargin);
        if (_capabilities.GetAvailableFreeBytes(paths.GameDirectory) < required)
        {
            throw new PatcherException($"Insufficient free disk space; {required} bytes are required.");
        }

        progress?.Report(new OperationProgress("prepare", "Building combined mod image."));
        PatchImageResult patched = PatchImageBuilder.Build(original, _build, _payloads);
        string backupPath = paths.ExecutablePath + $".modpack.{patched.OriginalSha256[..12]}.bak";
        if (File.Exists(backupPath) && HashFile(backupPath) != patched.OriginalSha256)
        {
            throw new PatcherException("Existing executable backup hash mismatch.");
        }
        if (!File.Exists(backupPath)) File.Copy(paths.ExecutablePath, backupPath);
        if (HashFile(backupPath) != patched.OriginalSha256)
        {
            throw new PatcherException("Executable backup failed verification.");
        }

        DateTimeOffset now = _clock().ToUniversalTime();
        string stamp = now.ToString("yyyyMMddTHHmmssZ");
        SaveBackupResult saveBackup = SaveBackupService.Create(
            paths.SaveDirectory,
            Path.Combine(paths.GameDirectory, $"kingdom_rush_modpack_saves_{stamp}.zip"));
        InstallManifest prepared = new(
            1, ModPackVersion, ReverseGoldVersion, FourHeroVersion, _build.BuildId, now,
            paths.ExecutablePath, backupPath, patched.OriginalSha256, patched.PatchedSha256,
            saveBackup.Path, saveBackup.Sha256, saveBackup.Files,
            ModPayloadSet.ReverseGoldSha256, ModPayloadSet.FourHeroSha256, "prepared");
        _manifests.WriteAtomic(paths.PendingManifestPath, prepared);
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(new OperationProgress("commit", "Replacing the game executable.", true));
        try
        {
            ReplaceAtomically(paths.ExecutablePath, patched.Image);
            if (HashFile(paths.ExecutablePath) != patched.PatchedSha256)
            {
                throw new PatcherException("Installed executable failed hash verification.");
            }
            InstallManifest installed = prepared with { TransactionState = "installed" };
            _manifests.WriteAtomic(paths.ManifestPath, installed);
            File.Delete(paths.PendingManifestPath);
            return Task.FromResult(installed);
        }
        catch
        {
            if (File.Exists(paths.ExecutablePath) && HashFile(paths.ExecutablePath) == patched.PatchedSha256)
            {
                ReplaceAtomically(paths.ExecutablePath, original);
            }
            if (File.Exists(paths.ManifestPath)) File.Delete(paths.ManifestPath);
            if (File.Exists(paths.PendingManifestPath)) File.Delete(paths.PendingManifestPath);
            throw;
        }
    }

    public Task<RecoveryResult> RecoverPendingAsync(InstallPaths paths, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(paths.PendingManifestPath)) return Task.FromResult(new RecoveryResult("none"));
        InstallManifest pending = _manifests.Read(paths.PendingManifestPath);
        string current = HashFile(paths.ExecutablePath);
        if (current == pending.PatchedSha256)
        {
            _manifests.WriteAtomic(paths.ManifestPath, pending with { TransactionState = "installed" });
            File.Delete(paths.PendingManifestPath);
            return Task.FromResult(new RecoveryResult("installed"));
        }
        if (current == pending.OriginalSha256)
        {
            File.Delete(paths.PendingManifestPath);
            return Task.FromResult(new RecoveryResult("not-installed"));
        }
        throw new RecoveryException("Pending transaction does not match the current executable.");
    }

    public Task<VerificationResult> VerifyAsync(InstallPaths paths, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RecoverPendingAsync(paths, cancellationToken).GetAwaiter().GetResult();
        InstallManifest manifest = _manifests.Read(paths.ManifestPath);
        ValidateInstalledManifest(manifest, paths);
        RequireHash(paths.ExecutablePath, manifest.PatchedSha256, "installed executable");
        RequireHash(manifest.BackupPath, manifest.OriginalSha256, "executable backup");
        RequireHash(manifest.SaveBackupPath, manifest.SaveBackupSha256, "save backup");
        SaveBackupService.Verify(manifest.SaveBackupPath, manifest.SaveBackupFiles);
        FusedArchive archive = FusedArchive.Read(File.ReadAllBytes(paths.ExecutablePath));
        RequireExactEntry(archive, "main.lua", _payloads.Bootstrap);
        RequireExactEntry(archive, "reverse_gold.lua", _payloads.ReverseGold);
        RequireExactEntry(archive, "multi_hero.lua", _payloads.FourHero);
        _ = archive.Entries.Single(entry =>
            string.Equals(entry.Name, "_modpack_main_orig.lua", StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(new VerificationResult("ok", manifest.PatchedSha256));
    }

    public Task<UninstallResult> UninstallAsync(
        InstallPaths paths,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RecoverPendingAsync(paths, cancellationToken).GetAwaiter().GetResult();
        InstallManifest manifest = _manifests.Read(paths.ManifestPath);
        ValidateInstalledManifest(manifest, paths);
        string current = HashFile(paths.ExecutablePath);
        if (current != manifest.PatchedSha256 && current != manifest.OriginalSha256)
        {
            throw new PatcherException("Refusing uninstall because the installed executable changed.");
        }
        RequireHash(manifest.BackupPath, manifest.OriginalSha256, "executable backup");
        progress?.Report(new OperationProgress("commit", "Restoring the original executable.", true));
        if (current != manifest.OriginalSha256)
        {
            ReplaceAtomically(paths.ExecutablePath, File.ReadAllBytes(manifest.BackupPath));
        }
        string archived = Path.Combine(
            paths.GameDirectory,
            $"kingdom_rush_modpack_manifest.uninstalled.{_clock().ToUniversalTime():yyyyMMddTHHmmssZ}.json");
        File.Move(paths.ManifestPath, archived);
        return Task.FromResult(new UninstallResult("uninstalled", HashFile(paths.ExecutablePath)));
    }

    private static void ReplaceAtomically(string path, byte[] data)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void RequireHash(string path, string expected, string label)
    {
        if (!File.Exists(path) || HashFile(path) != expected)
        {
            throw new PatcherException($"{label} is missing or has a SHA-256 mismatch.");
        }
    }

    private void ValidateInstalledManifest(InstallManifest manifest, InstallPaths paths)
    {
        bool identityMatches = manifest.TransactionState == "installed"
            && manifest.GameBuildId == _build.BuildId
            && manifest.ModPackVersion == ModPackVersion
            && manifest.ReverseGoldVersion == ReverseGoldVersion
            && manifest.FourHeroVersion == FourHeroVersion
            && manifest.ReverseGoldSha256 == ModPayloadSet.ReverseGoldSha256
            && manifest.FourHeroSha256 == ModPayloadSet.FourHeroSha256;
        string gameDirectory = Path.GetFullPath(paths.GameDirectory);
        bool pathsMatch = Path.GetFullPath(manifest.ExecutablePath).Equals(
                Path.GetFullPath(paths.ExecutablePath), StringComparison.OrdinalIgnoreCase)
            && IsDirectChild(manifest.BackupPath, gameDirectory)
            && IsDirectChild(manifest.SaveBackupPath, gameDirectory);
        if (!identityMatches || !pathsMatch)
        {
            throw new PatcherException("Installed manifest identity, version, or path is invalid.");
        }
    }

    private static bool IsDirectChild(string path, string parent) =>
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), parent, StringComparison.OrdinalIgnoreCase);

    private static void RequireExactEntry(FusedArchive archive, string name, byte[] expected)
    {
        FusedArchiveEntry entry = archive.Entries.Single(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (!entry.Data.AsSpan().SequenceEqual(expected))
        {
            throw new PatcherException($"Installed archive entry hash mismatch: {name}");
        }
    }
}
