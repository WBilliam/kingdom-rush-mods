using System.IO.Compression;
using System.Security.Cryptography;
using KingdomRush.ModPatcher.Core.Archives;
using KingdomRush.ModPatcher.Core.Compatibility;
using KingdomRush.ModPatcher.Core.Errors;
using KingdomRush.ModPatcher.Core.Discovery;
using KingdomRush.ModPatcher.Core.Payloads;
using KingdomRush.ModPatcher.Core.Transactions;
using Xunit;

namespace KingdomRush.ModPatcher.Core.Tests;

public sealed class ModPackServiceTests
{
    [Fact]
    public async Task InstallVerifyAndUninstallRestoreExactExecutableAndLeaveCurrentSaves()
    {
        using TemporaryInstall fixture = new();
        byte[] original = fixture.ExecutableBytes;
        ModPackService service = fixture.CreateService();

        InstallManifest installed = await service.InstallAsync(fixture.Paths, null, CancellationToken.None);
        VerificationResult verified = await service.VerifyAsync(fixture.Paths, CancellationToken.None);
        File.WriteAllText(Path.Combine(fixture.Paths.SaveDirectory, "slot.lua"), "new-progress");
        UninstallResult uninstalled = await service.UninstallAsync(fixture.Paths, null, CancellationToken.None);

        Assert.Equal("ok", verified.Status);
        Assert.Equal("uninstalled", uninstalled.Status);
        Assert.Equal(original, File.ReadAllBytes(fixture.Paths.ExecutablePath));
        Assert.Equal("new-progress", File.ReadAllText(Path.Combine(fixture.Paths.SaveDirectory, "slot.lua")));
        Assert.True(File.Exists(installed.SaveBackupPath));
        Assert.Equal(["nested/options.lua", "slot.lua"], installed.SaveBackupFiles);
        Assert.False(File.Exists(fixture.Paths.ManifestPath));
        Assert.Single(Directory.GetFiles(fixture.Paths.GameDirectory, "kingdom_rush_modpack_manifest.uninstalled.*.json"));
    }

    [Fact]
    public async Task InstallRefusesAnExistingManifest()
    {
        using TemporaryInstall fixture = new();
        File.WriteAllText(fixture.Paths.ManifestPath, "occupied");

        PatcherException error = await Assert.ThrowsAsync<PatcherException>(
            () => fixture.CreateService().InstallAsync(fixture.Paths, null, CancellationToken.None));

        Assert.Contains("already", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fixture.ExecutableBytes, File.ReadAllBytes(fixture.Paths.ExecutablePath));
    }

    [Fact]
    public async Task PreflightRejectsMissingSavesLockedExecutableAndLowDiskBeforeBackup()
    {
        using TemporaryInstall missing = new();
        Directory.Delete(missing.Paths.SaveDirectory, recursive: true);
        await Assert.ThrowsAsync<PatcherException>(
            () => missing.CreateService().InstallAsync(missing.Paths, null, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(missing.Paths.GameDirectory, "*.bak"));

        using TemporaryInstall locked = new();
        locked.Capabilities.Locked = true;
        await Assert.ThrowsAsync<IOException>(
            () => locked.CreateService().InstallAsync(locked.Paths, null, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(locked.Paths.GameDirectory, "*.bak"));

        using TemporaryInstall lowDisk = new();
        lowDisk.Capabilities.AvailableBytes = 1;
        await Assert.ThrowsAsync<PatcherException>(
            () => lowDisk.CreateService().InstallAsync(lowDisk.Paths, null, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(lowDisk.Paths.GameDirectory, "*.bak"));
    }

    [Fact]
    public async Task InstallHonorsCancellationBeforeCommitWithoutChangingExecutable()
    {
        using TemporaryInstall fixture = new();
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.CreateService().InstallAsync(fixture.Paths, null, cancelled.Token));

        Assert.Equal(fixture.ExecutableBytes, File.ReadAllBytes(fixture.Paths.ExecutablePath));
        Assert.False(File.Exists(fixture.Paths.PendingManifestPath));
    }

    [Fact]
    public async Task RecoveryFinalizesReplacementWhenPendingHashMatchesPatchedImage()
    {
        using TemporaryInstall fixture = new();
        ModPackService service = fixture.CreateService();
        await service.InstallAsync(fixture.Paths, null, CancellationToken.None);
        File.Move(fixture.Paths.ManifestPath, fixture.Paths.PendingManifestPath);

        RecoveryResult result = await service.RecoverPendingAsync(fixture.Paths, CancellationToken.None);

        Assert.Equal("installed", result.Status);
        Assert.True(File.Exists(fixture.Paths.ManifestPath));
        Assert.False(File.Exists(fixture.Paths.PendingManifestPath));
    }

    [Fact]
    public async Task RecoveryDiscardsPreparedJournalWhenOriginalIsStillPresent()
    {
        using TemporaryInstall fixture = new();
        new ManifestStore().WriteAtomic(fixture.Paths.PendingManifestPath, fixture.Manifest("prepared"));

        RecoveryResult result = await fixture.CreateService().RecoverPendingAsync(fixture.Paths, CancellationToken.None);

        Assert.Equal("not-installed", result.Status);
        Assert.False(File.Exists(fixture.Paths.PendingManifestPath));
        Assert.Equal(fixture.ExecutableBytes, File.ReadAllBytes(fixture.Paths.ExecutablePath));
    }

    [Fact]
    public async Task VerifyRejectsCorruptBackupAndUninstallRejectsChangedExecutable()
    {
        using TemporaryInstall corrupt = new();
        InstallManifest manifest = await corrupt.CreateService().InstallAsync(corrupt.Paths, null, CancellationToken.None);
        File.WriteAllBytes(manifest.BackupPath, [1, 2, 3]);
        await Assert.ThrowsAsync<PatcherException>(
            () => corrupt.CreateService().VerifyAsync(corrupt.Paths, CancellationToken.None));

        using TemporaryInstall changed = new();
        await changed.CreateService().InstallAsync(changed.Paths, null, CancellationToken.None);
        File.WriteAllBytes(changed.Paths.ExecutablePath, [9, 8, 7]);
        await Assert.ThrowsAsync<PatcherException>(
            () => changed.CreateService().UninstallAsync(changed.Paths, null, CancellationToken.None));
    }

    [Fact]
    public async Task FinalManifestFailureRollsBackTheExecutableAndClearsJournals()
    {
        using TemporaryInstall fixture = new();
        FailingFinalManifestStore manifests = new();
        ModPackService service = fixture.CreateService(manifests);

        await Assert.ThrowsAsync<IOException>(
            () => service.InstallAsync(fixture.Paths, null, CancellationToken.None));

        Assert.Equal(fixture.ExecutableBytes, File.ReadAllBytes(fixture.Paths.ExecutablePath));
        Assert.False(File.Exists(fixture.Paths.ManifestPath));
        Assert.False(File.Exists(fixture.Paths.PendingManifestPath));
    }

    [Fact]
    public async Task CancellationRaisedAtCommitDoesNotInterruptFinalization()
    {
        using TemporaryInstall fixture = new();
        using CancellationTokenSource cancellation = new();
        InlineProgress progress = new(value =>
        {
            if (value.IsCommitCritical) cancellation.Cancel();
        });

        InstallManifest result = await fixture.CreateService().InstallAsync(
            fixture.Paths, progress, cancellation.Token);

        Assert.Equal("installed", result.TransactionState);
        Assert.True(File.Exists(fixture.Paths.ManifestPath));
        Assert.False(File.Exists(fixture.Paths.PendingManifestPath));
    }

    [Fact]
    public async Task VerifyRejectsManifestFromDifferentInstallerOrModVersions()
    {
        using TemporaryInstall fixture = new();
        ModPackService service = fixture.CreateService();
        InstallManifest installed = await service.InstallAsync(fixture.Paths, null, CancellationToken.None);
        new ManifestStore().WriteAtomic(fixture.Paths.ManifestPath, installed with
        {
            ModPackVersion = "9.9.9",
            ReverseGoldVersion = "9.9.9",
        });

        await Assert.ThrowsAsync<PatcherException>(
            () => service.VerifyAsync(fixture.Paths, CancellationToken.None));
    }

    [Fact]
    public async Task VerifyRejectsBootstrapThatDoesNotMatchThisInstaller()
    {
        using TemporaryInstall fixture = new();
        ModPackService service = fixture.CreateService();
        InstallManifest installed = await service.InstallAsync(fixture.Paths, null, CancellationToken.None);
        byte[] modified = RewriteArchiveEntry(
            File.ReadAllBytes(fixture.Paths.ExecutablePath), "main.lua", "return 'tampered'\n"u8.ToArray());
        File.WriteAllBytes(fixture.Paths.ExecutablePath, modified);
        new ManifestStore().WriteAtomic(fixture.Paths.ManifestPath, installed with
        {
            PatchedSha256 = Convert.ToHexString(SHA256.HashData(modified)).ToLowerInvariant(),
        });

        await Assert.ThrowsAsync<PatcherException>(
            () => service.VerifyAsync(fixture.Paths, CancellationToken.None));
    }

    private static byte[] RewriteArchiveEntry(byte[] image, string name, byte[] replacement)
    {
        FusedArchive source = FusedArchive.Read(image);
        using MemoryStream zip = new();
        using (ZipArchive target = new(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (FusedArchiveEntry entry in source.Entries)
            {
                ZipArchiveEntry created = target.CreateEntry(entry.Name, CompressionLevel.Optimal);
                created.LastWriteTime = entry.LastWriteTime;
                created.ExternalAttributes = entry.ExternalAttributes;
                using Stream stream = created.Open();
                stream.Write(entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ? replacement : entry.Data);
            }
        }
        return [.. source.Prefix, .. zip.ToArray()];
    }
}

internal sealed class TemporaryInstall : IDisposable
{
    private static readonly byte[] OriginalMain = SyntheticFusedImage.Utf8("return {game='clean'}\n");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kr-patcher-tests", Guid.NewGuid().ToString("N"));

    internal TemporaryInstall()
    {
        Directory.CreateDirectory(_root);
        Paths = InstallPaths.ForGameDirectory(Path.Combine(_root, "game"), Path.Combine(_root, "appdata"));
        Directory.CreateDirectory(Paths.GameDirectory);
        Directory.CreateDirectory(Paths.SaveDirectory);
        Directory.CreateDirectory(Path.Combine(Paths.SaveDirectory, "nested"));
        File.WriteAllText(Path.Combine(Paths.SaveDirectory, "slot.lua"), "old-progress");
        File.WriteAllText(Path.Combine(Paths.SaveDirectory, "nested", "options.lua"), "options");
        ExecutableBytes = SyntheticFusedImage.Create(
            ("main.lua", OriginalMain),
            ("assets/sentinel.bin", [1, 2, 3, 4]));
        File.WriteAllBytes(Paths.ExecutablePath, ExecutableBytes);
        Build = new SupportedBuild("test-build", "test-version", Sha256(ExecutableBytes), Sha256(OriginalMain));
    }

    internal string Root => _root;
    internal InstallPaths Paths { get; }
    internal byte[] ExecutableBytes { get; }
    internal SupportedBuild Build { get; }
    internal FakeCapabilities Capabilities { get; } = new();

    internal ModPackService CreateService(ManifestStore? manifests = null) => new(
        Build,
        EmbeddedPayloadProvider.Load(),
        Capabilities,
        manifests ?? new ManifestStore(),
        () => new DateTimeOffset(2026, 10, 17, 1, 2, 3, TimeSpan.Zero));

    internal GameDiscoveryService CreateDiscovery(IRegistryReader registry, string defaultSteamPath) => new(
        registry,
        CreateService(),
        Path.GetDirectoryName(Paths.SaveDirectory)!,
        defaultSteamPath,
        Build);

    internal InstallManifest Manifest(string state) => new(
        1, "0.1.0", "1.0.1", "0.1.4", Build.BuildId,
        new DateTimeOffset(2026, 10, 17, 1, 2, 3, TimeSpan.Zero),
        Paths.ExecutablePath, Paths.ExecutablePath + ".test.bak",
        Build.ExecutableSha256, new string('a', 64),
        Path.Combine(Paths.GameDirectory, "save.zip"), new string('b', 64),
        ["nested/options.lua", "slot.lua"],
        ModPayloadSet.ReverseGoldSha256, ModPayloadSet.FourHeroSha256, state);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal sealed class FailingFinalManifestStore : ManifestStore
{
    private int _writes;

    public override void WriteAtomic(string path, InstallManifest manifest)
    {
        _writes++;
        if (_writes == 2) throw new IOException("simulated final manifest failure");
        base.WriteAtomic(path, manifest);
    }
}

internal sealed class InlineProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
{
    public void Report(OperationProgress value) => report(value);
}

internal sealed class FakeCapabilities : IFileSystemCapabilities
{
    internal bool Locked { get; set; }
    internal long AvailableBytes { get; set; } = long.MaxValue;

    public void EnsureExclusiveWrite(string path)
    {
        if (Locked)
        {
            throw new IOException("locked");
        }
    }

    public long GetAvailableFreeBytes(string destinationPath) => AvailableBytes;
}
