using KingdomRush.ModPatcher.Core.Discovery;
using KingdomRush.ModPatcher.Core.Transactions;
using KingdomRush.ModPatcher.WinForms;
using Xunit;

namespace KingdomRush.ModPatcher.WinForms.Tests;

public sealed class MainPresenterTests
{
    [Fact]
    public async Task InitializeDiscoversFirstCandidateAndRendersReadyState()
    {
        FakeView view = new();
        FakeBackend backend = new() { Candidates = [@"D:\Games\Kingdom Rush"] };
        MainPresenter presenter = new(view, backend, (_, _) => { });
        int callingThread = Environment.CurrentManagedThreadId;

        await presenter.InitializeAsync(CancellationToken.None);

        Assert.NotEqual(callingThread, backend.FindCandidatesThreadId);
        Assert.Equal(@"D:\Games\Kingdom Rush", view.LastState.GameDirectory);
        Assert.True(view.LastState.CanInstall);
        Assert.False(view.LastState.CanVerify);
        Assert.Contains("Supported", view.LastState.GameStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BrowseUsesSelectedManualDirectory()
    {
        FakeView view = new() { ChosenDirectory = @"E:\Manual\Kingdom Rush" };
        FakeBackend backend = new();
        MainPresenter presenter = new(view, backend, (_, _) => { });

        await presenter.BrowseSelectedAsync(CancellationToken.None);

        Assert.Equal(view.ChosenDirectory, backend.LastInspected);
        Assert.Equal(view.ChosenDirectory, view.LastState.GameDirectory);
    }

    [Theory]
    [InlineData(InstallationState.Missing, false, false, false)]
    [InlineData(InstallationState.Unsupported, false, false, false)]
    [InlineData(InstallationState.ReadyToInstall, true, false, false)]
    [InlineData(InstallationState.InstalledHealthy, false, true, true)]
    [InlineData(InstallationState.InstalledCorrupt, false, true, true)]
    [InlineData(InstallationState.RecoveryRequired, false, true, false)]
    public void ViewStateEnablesOnlyActionsValidForStatus(
        InstallationState state, bool install, bool verify, bool uninstall)
    {
        InstallerViewState viewState = InstallerViewState.FromStatus(
            @"C:\Game", new InstallationStatus(state, "detail"));

        Assert.Equal(install, viewState.CanInstall);
        Assert.Equal(verify, viewState.CanVerify);
        Assert.Equal(uninstall, viewState.CanUninstall);
    }

    [Fact]
    public async Task InstallRequiresConfirmationDisablesActionsAndRefreshesStatus()
    {
        FakeView view = new() { Confirmation = true };
        FakeBackend backend = new();
        MainPresenter presenter = new(view, backend, (_, _) => { });
        await presenter.BrowseSelectedAsync(@"C:\Game", CancellationToken.None);

        await presenter.InstallAsync(CancellationToken.None);

        Assert.Equal(1, backend.InstallCalls);
        Assert.Contains(view.States, state => state.IsBusy && !state.CanInstall && !state.CanVerify && !state.CanUninstall);
        Assert.Equal(InstallationState.InstalledHealthy, backend.Status.State);
        Assert.True(view.LastState.CanVerify);
    }

    [Fact]
    public async Task DeclinedInstallDoesNotCallBackend()
    {
        FakeView view = new() { Confirmation = false };
        FakeBackend backend = new();
        MainPresenter presenter = new(view, backend, (_, _) => { });
        await presenter.BrowseSelectedAsync(@"C:\Game", CancellationToken.None);

        await presenter.InstallAsync(CancellationToken.None);

        Assert.Equal(0, backend.InstallCalls);
    }

    [Fact]
    public async Task FailureRestoresActionsAndLogsActionableErrorWithoutSaveContents()
    {
        FakeView view = new();
        FakeBackend backend = new() { VerifyError = new UnauthorizedAccessException("access denied") };
        List<string> logs = [];
        MainPresenter presenter = new(view, backend, (message, _) => logs.Add(message));
        backend.Status = new InstallationStatus(InstallationState.InstalledHealthy, "installed");
        await presenter.BrowseSelectedAsync(@"C:\Game", CancellationToken.None);

        await presenter.VerifyAsync(CancellationToken.None);

        Assert.Contains("permission", view.LastState.GameStatus, StringComparison.OrdinalIgnoreCase);
        Assert.True(view.LastState.CanVerify);
        Assert.Contains(logs, line => line.Contains(@"C:\Game", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, line => line.Contains("save contents", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InstallWorkRunsAwayFromTheCallingUiThread()
    {
        FakeView view = new() { Confirmation = true };
        FakeBackend backend = new();
        MainPresenter presenter = new(view, backend, (_, _) => { });
        await presenter.BrowseSelectedAsync(@"C:\Game", CancellationToken.None);
        int callingThread = Environment.CurrentManagedThreadId;

        await presenter.InstallAsync(CancellationToken.None);

        Assert.NotEqual(callingThread, backend.InstallThreadId);
    }

    [Fact]
    public async Task LifetimeCancellationPropagatesWithoutRenderingAnOperationFailure()
    {
        FakeView view = new();
        FakeBackend backend = new()
        {
            Status = new InstallationStatus(InstallationState.InstalledHealthy, "installed"),
        };
        MainPresenter presenter = new(view, backend, (_, _) => { });
        await presenter.BrowseSelectedAsync(@"C:\Game", CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => presenter.VerifyAsync(cancellation.Token));

        Assert.DoesNotContain(view.States, state => state.GameStatus.StartsWith("Operation failed", StringComparison.Ordinal));
    }
}

internal sealed class FakeView : IInstallerView
{
    internal List<InstallerViewState> States { get; } = [];
    internal InstallerViewState LastState => States[^1];
    internal string? ChosenDirectory { get; set; } = @"C:\Game";
    internal bool Confirmation { get; set; } = true;

    public void Render(InstallerViewState state) => States.Add(state);
    public void AppendLog(string line) { }
    public string? ChooseGameDirectory() => ChosenDirectory;
    public bool Confirm(string message) => Confirmation;
}

internal sealed class FakeBackend : IInstallerBackend
{
    internal IReadOnlyList<string> Candidates { get; set; } = [];
    internal InstallationStatus Status { get; set; } = new(InstallationState.ReadyToInstall, "Supported clean build");
    internal string? LastInspected { get; private set; }
    internal int InstallCalls { get; private set; }
    internal int InstallThreadId { get; private set; }
    internal int FindCandidatesThreadId { get; private set; }
    internal Exception? VerifyError { get; set; }

    public IReadOnlyList<string> FindCandidates()
    {
        FindCandidatesThreadId = Environment.CurrentManagedThreadId;
        return Candidates;
    }
    public Task<InstallationStatus> InspectAsync(string gameDirectory, CancellationToken token)
    {
        LastInspected = gameDirectory;
        return Task.FromResult(Status);
    }
    public Task InstallAsync(string gameDirectory, IProgress<OperationProgress> progress, CancellationToken token)
    {
        InstallCalls++;
        InstallThreadId = Environment.CurrentManagedThreadId;
        progress.Report(new OperationProgress("commit", "commit", true));
        Status = new InstallationStatus(InstallationState.InstalledHealthy, "installed");
        return Task.CompletedTask;
    }
    public Task VerifyAsync(string gameDirectory, CancellationToken token) =>
        VerifyError is null ? Task.CompletedTask : Task.FromException(VerifyError);
    public Task UninstallAsync(string gameDirectory, IProgress<OperationProgress> progress, CancellationToken token) => Task.CompletedTask;
}
