namespace KingdomRush.ModPatcher.Core.Discovery;

public enum InstallationState
{
    Missing,
    Unsupported,
    ReadyToInstall,
    InstalledHealthy,
    InstalledCorrupt,
    RecoveryRequired,
}

public sealed record InstallationStatus(
    InstallationState State,
    string Detail,
    string? TechnicalDetail = null);
