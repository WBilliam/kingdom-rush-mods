namespace KingdomRush.ModPatcher.WinForms;

public partial class MainForm : Form, IInstallerView
{
    private MainPresenter? _presenter;
    private bool _commitCritical;
    private readonly CancellationTokenSource _lifetime = new();

    public MainForm()
    {
        InitializeComponent();
        browseButton.Click += async (_, _) => await RunAsync(token => _presenter!.BrowseSelectedAsync(token));
        installButton.Click += async (_, _) => await RunAsync(token => _presenter!.InstallAsync(token));
        verifyButton.Click += async (_, _) => await RunAsync(token => _presenter!.VerifyAsync(token));
        uninstallButton.Click += async (_, _) => await RunAsync(token => _presenter!.UninstallAsync(token));
        FormClosing += OnFormClosing;
    }

    public void Attach(MainPresenter presenter) => _presenter = presenter;

    public async Task InitializeAsync() => await RunAsync(token => _presenter!.InitializeAsync(token));

    public void Render(InstallerViewState state)
    {
        if (InvokeRequired) { BeginInvoke(() => Render(state)); return; }
        gamePathTextBox.Text = state.GameDirectory;
        gameStatusLabel.Text = state.GameStatus;
        reverseStatusLabel.Text = $"Reverse Gold: {state.ReverseGoldStatus}";
        heroStatusLabel.Text = $"Four-Hero: {state.FourHeroStatus}";
        installButton.Enabled = state.CanInstall;
        verifyButton.Enabled = state.CanVerify;
        uninstallButton.Enabled = state.CanUninstall;
        browseButton.Enabled = !state.IsBusy;
        progressBar.Value = Math.Clamp(state.ProgressPercent, 0, 100);
        _commitCritical = state.IsCommitCritical;
    }

    public void AppendLog(string line)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendLog(line)); return; }
        logTextBox.AppendText($"{DateTime.Now:T} {line}{Environment.NewLine}");
    }

    public string? ChooseGameDirectory()
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "Choose the Kingdom Rush game folder",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(gamePathTextBox.Text) ? gamePathTextBox.Text : string.Empty,
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
    }

    public bool Confirm(string message) =>
        MessageBox.Show(this, message, "Kingdom Rush Mod Pack", MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        try { await action(_lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            InstallerLog.Write("Unhandled UI operation error.", error);
            MessageBox.Show(this, error.Message, "Operation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_commitCritical)
        {
            eventArgs.Cancel = true;
            MessageBox.Show(this, "Please wait while the game executable is safely replaced.",
                "Operation in progress", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _lifetime.Cancel();
    }
}
