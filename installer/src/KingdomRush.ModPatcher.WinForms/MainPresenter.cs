using KingdomRush.ModPatcher.Core.Discovery;
using KingdomRush.ModPatcher.Core.Transactions;

namespace KingdomRush.ModPatcher.WinForms;

public interface IInstallerBackend
{
    IReadOnlyList<string> FindCandidates();
    Task<InstallationStatus> InspectAsync(string gameDirectory, CancellationToken token);
    Task InstallAsync(string gameDirectory, IProgress<OperationProgress> progress, CancellationToken token);
    Task VerifyAsync(string gameDirectory, CancellationToken token);
    Task UninstallAsync(string gameDirectory, IProgress<OperationProgress> progress, CancellationToken token);
}

public sealed class InstallerBackend(
    GameDiscoveryService discovery,
    ModPackService modPack,
    string appDataDirectory) : IInstallerBackend
{
    public IReadOnlyList<string> FindCandidates() => discovery.FindCandidates();
    public Task<InstallationStatus> InspectAsync(string gameDirectory, CancellationToken token) =>
        discovery.InspectAsync(gameDirectory, token);
    public async Task InstallAsync(string gameDirectory, IProgress<OperationProgress> progress, CancellationToken token) =>
        _ = await modPack.InstallAsync(InstallPaths.ForGameDirectory(gameDirectory, appDataDirectory), progress, token);
    public async Task VerifyAsync(string gameDirectory, CancellationToken token) =>
        _ = await modPack.VerifyAsync(InstallPaths.ForGameDirectory(gameDirectory, appDataDirectory), token);
    public async Task UninstallAsync(string gameDirectory, IProgress<OperationProgress> progress, CancellationToken token) =>
        _ = await modPack.UninstallAsync(InstallPaths.ForGameDirectory(gameDirectory, appDataDirectory), progress, token);
}

public sealed class MainPresenter
{
    private readonly IInstallerView _view;
    private readonly IInstallerBackend _backend;
    private readonly Action<string, Exception?> _log;
    private InstallerViewState _state = new("", "Searching for Kingdom Rush…", "Unknown", "Unknown", false, false, false);

    public MainPresenter(IInstallerView view, IInstallerBackend backend, Action<string, Exception?> log)
    {
        _view = view;
        _backend = backend;
        _log = log;
    }

    public async Task InitializeAsync(CancellationToken token)
    {
        IReadOnlyList<string> candidates = await Task.Run(_backend.FindCandidates, token);
        string? candidate = candidates.FirstOrDefault();
        if (candidate is null)
        {
            _state = InstallerViewState.FromStatus("", new InstallationStatus(InstallationState.Missing, "Kingdom Rush was not detected. Choose its folder."));
            _view.Render(_state);
            return;
        }
        await BrowseSelectedAsync(candidate, token);
    }

    public async Task BrowseSelectedAsync(CancellationToken token)
    {
        string? selected = _view.ChooseGameDirectory();
        if (!string.IsNullOrWhiteSpace(selected)) await BrowseSelectedAsync(selected, token);
    }

    public async Task BrowseSelectedAsync(string gameDirectory, CancellationToken token)
    {
        InstallationStatus status = await Task.Run(
            () => _backend.InspectAsync(gameDirectory, token), token);
        _state = InstallerViewState.FromStatus(gameDirectory, status);
        _view.Render(_state);
        _log($"Inspected game directory: {gameDirectory} ({status.State})", null);
    }

    public async Task InstallAsync(CancellationToken token)
    {
        if (!_state.CanInstall || !_view.Confirm("Install Reverse Gold and Four-Hero? Backups will be created first.")) return;
        await RunOperationAsync(
            (progress, ct) => _backend.InstallAsync(_state.GameDirectory, progress, ct),
            "Installing both mods…", token);
    }

    public async Task VerifyAsync(CancellationToken token)
    {
        if (!_state.CanVerify) return;
        await RunOperationAsync(
            (_, ct) => _backend.VerifyAsync(_state.GameDirectory, ct),
            "Verifying installation…", token);
    }

    public async Task UninstallAsync(CancellationToken token)
    {
        if (!_state.CanUninstall || !_view.Confirm("Restore the original executable? Current saves will be kept.")) return;
        await RunOperationAsync(
            (progress, ct) => _backend.UninstallAsync(_state.GameDirectory, progress, ct),
            "Restoring the original game…", token);
    }

    private async Task RunOperationAsync(
        Func<IProgress<OperationProgress>, CancellationToken, Task> operation,
        string message,
        CancellationToken token)
    {
        string directory = _state.GameDirectory;
        _state = _state.Busy(message, progress: 10);
        _view.Render(_state);
        InlineProgress progress = new(value =>
        {
            _state = _state.Busy(value.Message, value.IsCommitCritical, value.IsCommitCritical ? 75 : 40);
            _view.Render(_state);
            _view.AppendLog(value.Message);
        });
        try
        {
            await Task.Run(() => operation(progress, token), token);
            await BrowseSelectedAsync(directory, CancellationToken.None);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            string guidance = error is UnauthorizedAccessException
                ? "Permission denied. Close the game and run the installer with access to the Steam folder."
                : $"Operation failed: {error.Message}";
            _log($"Operation failed for {directory}", error);
            InstallationStatus inspected = await _backend.InspectAsync(directory, CancellationToken.None);
            _state = InstallerViewState.FromStatus(directory, inspected) with { GameStatus = guidance };
            _view.Render(_state);
            _view.AppendLog(guidance);
        }
    }

    private sealed class InlineProgress(Action<OperationProgress> callback) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => callback(value);
    }
}
