using KingdomRush.ModPatcher.Core.Archives;
using KingdomRush.ModPatcher.Core.Errors;
using KingdomRush.ModPatcher.Core.Payloads;
using KingdomRush.ModPatcher.Core.Transactions;
using Xunit;

namespace KingdomRush.ModPatcher.Core.Tests;

public sealed class EndToEndAcceptanceTests
{
    [Fact]
    public async Task CombinedInstallerCompletesTheFullLifecycleWithoutChangingSaves()
    {
        using TemporaryInstall fixture = new();
        byte[] originalExecutable = File.ReadAllBytes(fixture.Paths.ExecutablePath);
        string originalSave = File.ReadAllText(Path.Combine(fixture.Paths.SaveDirectory, "slot.lua"));
        ModPackService service = fixture.CreateService();

        InstallManifest manifest = await service.InstallAsync(
            fixture.Paths, progress: null, CancellationToken.None);

        FusedArchive installed = FusedArchive.Read(File.ReadAllBytes(fixture.Paths.ExecutablePath));
        ModPayloadSet payloads = EmbeddedPayloadProvider.Load();
        Assert.Equal(payloads.Bootstrap, Entry(installed, "main.lua"));
        Assert.Equal(payloads.ReverseGold, Entry(installed, "reverse_gold.lua"));
        Assert.Equal(payloads.FourHero, Entry(installed, "multi_hero.lua"));
        Assert.True(File.Exists(manifest.BackupPath));
        Assert.True(File.Exists(manifest.SaveBackupPath));
        Assert.Equal("ok", (await service.VerifyAsync(fixture.Paths, CancellationToken.None)).Status);

        byte[] healthyPatchedExecutable = File.ReadAllBytes(fixture.Paths.ExecutablePath);
        File.WriteAllBytes(fixture.Paths.ExecutablePath, [0x4d, 0x5a, 0x00]);
        await Assert.ThrowsAsync<PatcherException>(
            () => service.VerifyAsync(fixture.Paths, CancellationToken.None));
        File.WriteAllBytes(fixture.Paths.ExecutablePath, healthyPatchedExecutable);

        Assert.Equal("uninstalled", (
            await service.UninstallAsync(fixture.Paths, progress: null, CancellationToken.None)).Status);
        Assert.Equal(originalExecutable, File.ReadAllBytes(fixture.Paths.ExecutablePath));
        Assert.Equal(originalSave, File.ReadAllText(Path.Combine(fixture.Paths.SaveDirectory, "slot.lua")));
    }

    private static byte[] Entry(FusedArchive archive, string name) =>
        Assert.Single(archive.Entries, entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Data;
}
