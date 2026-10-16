using Microsoft.Win32;
using System.Runtime.Versioning;
using KingdomRush.ModPatcher.Core.Discovery;
using KingdomRush.ModPatcher.Core.Transactions;
using Xunit;

namespace KingdomRush.ModPatcher.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class GameDiscoveryServiceTests
{
    [Fact]
    public void FindCandidatesUsesBothRegistryViewsVdfAndDefaultWithoutDuplicates()
    {
        using TemporaryInstall fixture = new();
        string steam32 = Path.Combine(fixture.Root, "steam32");
        string steam64 = Path.Combine(fixture.Root, "steam64");
        string extra = Path.Combine(fixture.Root, "extra");
        Directory.CreateDirectory(Path.Combine(steam32, "steamapps"));
        Directory.CreateDirectory(Path.Combine(steam64, "steamapps"));
        File.WriteAllText(Path.Combine(steam32, "steamapps", "libraryfolders.vdf"),
            $"\"libraryfolders\" {{ \"1\" {{ \"path\" \"{extra.Replace("\\", "\\\\")}\" }} }}");
        FakeRegistry registry = new(steam32, steam64);
        GameDiscoveryService service = fixture.CreateDiscovery(registry, steam32);

        IReadOnlyList<string> candidates = service.FindCandidates();

        Assert.Contains(Path.Combine(steam32, "steamapps", "common", "Kingdom Rush"), candidates);
        Assert.Contains(Path.Combine(steam64, "steamapps", "common", "Kingdom Rush"), candidates);
        Assert.Contains(Path.Combine(extra, "steamapps", "common", "Kingdom Rush"), candidates);
        Assert.Equal(candidates.Count, candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(registry.Reads, read => read.View == RegistryView.Registry32);
        Assert.Contains(registry.Reads, read => read.View == RegistryView.Registry64);
    }

    [Fact]
    public void FindCandidatesSkipsMalformedAndInaccessibleLibraries()
    {
        using TemporaryInstall fixture = new();
        string steam = Path.Combine(fixture.Root, "steam");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"path\" \"unterminated");

        IReadOnlyList<string> candidates = fixture.CreateDiscovery(new FakeRegistry(steam, null), steam).FindCandidates();

        Assert.Single(candidates);
    }

    [Fact]
    public async Task InspectClassifiesMissingUnsupportedCleanInstalledCorruptAndPending()
    {
        using TemporaryInstall fixture = new();
        GameDiscoveryService service = fixture.CreateDiscovery(new FakeRegistry(null, null), fixture.Root);
        string missing = Path.Combine(fixture.Root, "manual-missing");
        Assert.Equal(InstallationState.Missing, (await service.InspectAsync(missing, CancellationToken.None)).State);

        string unsupported = Path.Combine(fixture.Root, "unsupported");
        Directory.CreateDirectory(unsupported);
        File.WriteAllBytes(Path.Combine(unsupported, "Kingdom Rush.exe"), [1, 2, 3]);
        Assert.Equal(InstallationState.Unsupported, (await service.InspectAsync(unsupported, CancellationToken.None)).State);

        Assert.Equal(InstallationState.ReadyToInstall, (await service.InspectAsync(fixture.Paths.GameDirectory, CancellationToken.None)).State);
        await fixture.CreateService().InstallAsync(fixture.Paths, null, CancellationToken.None);
        Assert.Equal(InstallationState.InstalledHealthy, (await service.InspectAsync(fixture.Paths.GameDirectory, CancellationToken.None)).State);

        File.WriteAllBytes(fixture.Paths.ExecutablePath, [9, 9, 9]);
        InstallationStatus corrupt = await service.InspectAsync(fixture.Paths.GameDirectory, CancellationToken.None);
        Assert.Equal(InstallationState.InstalledCorrupt, corrupt.State);
        Assert.NotNull(corrupt.TechnicalDetail);

        using TemporaryInstall pending = new();
        new ManifestStore().WriteAtomic(pending.Paths.PendingManifestPath, pending.Manifest("prepared"));
        Assert.Equal(
            InstallationState.RecoveryRequired,
            (await pending.CreateDiscovery(new FakeRegistry(null, null), pending.Root)
                .InspectAsync(pending.Paths.GameDirectory, CancellationToken.None)).State);
    }
}

[SupportedOSPlatform("windows")]
internal sealed class FakeRegistry(string? registry32, string? registry64) : IRegistryReader
{
    internal List<(RegistryView View, string ValueName)> Reads { get; } = [];

    public string? ReadString(RegistryHive hive, RegistryView view, string subKey, string valueName)
    {
        Reads.Add((view, valueName));
        return view == RegistryView.Registry32 ? registry32 : registry64;
    }
}
