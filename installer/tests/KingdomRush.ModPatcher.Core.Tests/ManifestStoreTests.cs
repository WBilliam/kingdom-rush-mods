using System.Text.Json;
using KingdomRush.ModPatcher.Core.Errors;
using KingdomRush.ModPatcher.Core.Transactions;
using Xunit;

namespace KingdomRush.ModPatcher.Core.Tests;

public sealed class ManifestStoreTests
{
    [Fact]
    public void AtomicRoundTripPreservesSchemaAndBackupMetadata()
    {
        using TemporaryInstall fixture = new();
        ManifestStore store = new();
        InstallManifest expected = fixture.Manifest("prepared");

        store.WriteAtomic(fixture.Paths.PendingManifestPath, expected);
        InstallManifest actual = store.Read(fixture.Paths.PendingManifestPath);

        Assert.Equal(1, actual.SchemaVersion);
        Assert.Equal(expected.ModPackVersion, actual.ModPackVersion);
        Assert.Equal(expected.ExecutablePath, actual.ExecutablePath);
        Assert.Equal(expected.OriginalSha256, actual.OriginalSha256);
        Assert.Equal(expected.TransactionState, actual.TransactionState);
        Assert.Equal(expected.SaveBackupFiles, actual.SaveBackupFiles);
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.tmp"));
    }

    [Fact]
    public void ReadRejectsUnsupportedSchema()
    {
        using TemporaryInstall fixture = new();
        InstallManifest manifest = fixture.Manifest("installed") with { SchemaVersion = 99 };
        File.WriteAllText(fixture.Paths.ManifestPath, JsonSerializer.Serialize(manifest));

        RecoveryException error = Assert.Throws<RecoveryException>(
            () => new ManifestStore().Read(fixture.Paths.ManifestPath));

        Assert.Contains("schema", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadRejectsTruncatedPendingJsonWithoutChangingIt()
    {
        using TemporaryInstall fixture = new();
        const string truncated = "{\"schemaVersion\":1,";
        File.WriteAllText(fixture.Paths.PendingManifestPath, truncated);

        Assert.Throws<RecoveryException>(() => new ManifestStore().Read(fixture.Paths.PendingManifestPath));

        Assert.Equal(truncated, File.ReadAllText(fixture.Paths.PendingManifestPath));
    }
}
