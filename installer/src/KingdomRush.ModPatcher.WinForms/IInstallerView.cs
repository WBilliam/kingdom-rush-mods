namespace KingdomRush.ModPatcher.WinForms;

public interface IInstallerView
{
    void Render(InstallerViewState state);
    void AppendLog(string line);
    string? ChooseGameDirectory();
    bool Confirm(string message);
}
