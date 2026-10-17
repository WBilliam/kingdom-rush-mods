using KingdomRush.ModPatcher.Core.Discovery;

namespace KingdomRush.ModPatcher.WinForms;

public sealed record InstallerViewState(
    string GameDirectory,
    string GameStatus,
    string ReverseGoldStatus,
    string FourHeroStatus,
    bool CanInstall,
    bool CanVerify,
    bool CanUninstall,
    bool IsBusy = false,
    bool IsCommitCritical = false,
    int ProgressPercent = 0)
{
    public static InstallerViewState FromStatus(string gameDirectory, InstallationStatus status)
    {
        bool installed = status.State is InstallationState.InstalledHealthy or InstallationState.InstalledCorrupt;
        return new InstallerViewState(
            gameDirectory,
            status.Detail,
            installed ? "Installed" : "Not installed",
            installed ? "Installed" : "Not installed",
            status.State == InstallationState.ReadyToInstall,
            status.State is InstallationState.InstalledHealthy or InstallationState.InstalledCorrupt or InstallationState.RecoveryRequired,
            installed);
    }

    public InstallerViewState Busy(string message, bool commitCritical = false, int progress = 0) => this with
    {
        GameStatus = message,
        CanInstall = false,
        CanVerify = false,
        CanUninstall = false,
        IsBusy = true,
        IsCommitCritical = commitCritical,
        ProgressPercent = progress,
    };
}
