using KingdomRush.ModPatcher.Core.Compatibility;
using KingdomRush.ModPatcher.Core.Discovery;
using KingdomRush.ModPatcher.Core.Payloads;
using KingdomRush.ModPatcher.Core.Transactions;

namespace KingdomRush.ModPatcher.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, eventArgs) => InstallerLog.Write("UI thread exception.", eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            InstallerLog.Write("Unhandled application exception.", eventArgs.ExceptionObject as Exception);
        try
        {
            ModPayloadSet payloads = EmbeddedPayloadProvider.Load();
            SupportedBuild build = SupportedBuild.KingdomRushSteam24662480;
            ModPackService modPack = new(build, payloads, new PhysicalFileSystemCapabilities(), new ManifestStore());
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string defaultSteam = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            GameDiscoveryService discovery = new(new WindowsRegistryReader(), modPack, appData, defaultSteam, build);
            InstallerBackend backend = new(discovery, modPack, appData);
            MainForm form = new();
            MainPresenter presenter = new(form, backend, InstallerLog.Write);
            form.Attach(presenter);
            form.Shown += async (_, _) =>
            {
#if DEBUG
                if (args.Length == 2 && args[0] == "--game-dir")
                    await presenter.BrowseSelectedAsync(args[1], CancellationToken.None);
                else
#endif
                    await form.InitializeAsync();
            };
            Application.Run(form);
        }
        catch (Exception error)
        {
            InstallerLog.Write("Startup failed.", error);
            MessageBox.Show(error.Message, "Kingdom Rush Mod Pack could not start",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
