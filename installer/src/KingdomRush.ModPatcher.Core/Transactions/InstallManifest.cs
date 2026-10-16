namespace KingdomRush.ModPatcher.Core.Transactions;

public sealed record InstallManifest(
    int SchemaVersion,
    string ModPackVersion,
    string ReverseGoldVersion,
    string FourHeroVersion,
    string GameBuildId,
    DateTimeOffset InstalledAtUtc,
    string ExecutablePath,
    string BackupPath,
    string OriginalSha256,
    string PatchedSha256,
    string SaveBackupPath,
    string SaveBackupSha256,
    IReadOnlyList<string> SaveBackupFiles,
    string ReverseGoldSha256,
    string FourHeroSha256,
    string TransactionState);

public sealed record VerificationResult(string Status, string PatchedSha256);
public sealed record UninstallResult(string Status, string RestoredSha256);
public sealed record RecoveryResult(string Status);
