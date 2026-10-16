#nullable enable

namespace KingdomRush.ModPatcher.WinForms;

partial class MainForm
{
    private System.ComponentModel.IContainer? components;
    private Label titleLabel = null!;
    private TextBox gamePathTextBox = null!;
    private Button browseButton = null!;
    private Label gameStatusLabel = null!;
    private Label reverseStatusLabel = null!;
    private Label heroStatusLabel = null!;
    private Button installButton = null!;
    private Button verifyButton = null!;
    private Button uninstallButton = null!;
    private ProgressBar progressBar = null!;
    private TextBox logTextBox = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        titleLabel = new Label();
        gamePathTextBox = new TextBox();
        browseButton = new Button();
        gameStatusLabel = new Label();
        reverseStatusLabel = new Label();
        heroStatusLabel = new Label();
        installButton = new Button();
        verifyButton = new Button();
        uninstallButton = new Button();
        progressBar = new ProgressBar();
        logTextBox = new TextBox();
        SuspendLayout();

        titleLabel.AutoSize = true;
        titleLabel.Font = new Font("Segoe UI", 17F, FontStyle.Bold);
        titleLabel.Location = new Point(24, 20);
        titleLabel.Text = "Kingdom Rush Mod Pack";

        gamePathTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        gamePathTextBox.Location = new Point(24, 68);
        gamePathTextBox.ReadOnly = true;
        gamePathTextBox.Size = new Size(590, 27);

        browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browseButton.Location = new Point(624, 67);
        browseButton.Size = new Size(100, 30);
        browseButton.Text = "Browse…";

        gameStatusLabel.Location = new Point(24, 112);
        gameStatusLabel.Size = new Size(700, 40);
        gameStatusLabel.Text = "Searching for Kingdom Rush…";
        reverseStatusLabel.Location = new Point(24, 158);
        reverseStatusLabel.Size = new Size(340, 25);
        reverseStatusLabel.Text = "Reverse Gold: Unknown";
        heroStatusLabel.Location = new Point(384, 158);
        heroStatusLabel.Size = new Size(340, 25);
        heroStatusLabel.Text = "Four-Hero: Unknown";

        installButton.Location = new Point(24, 202);
        installButton.Size = new Size(210, 38);
        installButton.Text = "Install Both Mods";
        verifyButton.Location = new Point(252, 202);
        verifyButton.Size = new Size(210, 38);
        verifyButton.Text = "Verify";
        uninstallButton.Location = new Point(480, 202);
        uninstallButton.Size = new Size(244, 38);
        uninstallButton.Text = "Uninstall / Restore";

        progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        progressBar.Location = new Point(24, 258);
        progressBar.Size = new Size(700, 18);

        logTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        logTextBox.Location = new Point(24, 294);
        logTextBox.Multiline = true;
        logTextBox.ReadOnly = true;
        logTextBox.ScrollBars = ScrollBars.Vertical;
        logTextBox.Size = new Size(700, 220);

        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(748, 540);
        Controls.AddRange([titleLabel, gamePathTextBox, browseButton, gameStatusLabel,
            reverseStatusLabel, heroStatusLabel, installButton, verifyButton, uninstallButton,
            progressBar, logTextBox]);
        MinimumSize = new Size(660, 500);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Kingdom Rush Mod Pack Setup";
        ResumeLayout(false);
        PerformLayout();
    }
}
