using System.Text;

namespace KingdomRush.ModPatcher.WinForms;

public static class InstallerLog
{
    private static readonly object Gate = new();

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KingdomRushModPatcher",
        "installer.log");

    public static void Write(string message, Exception? error = null)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            string line = $"{DateTimeOffset.UtcNow:O} {message}";
            if (error is not null) line += Environment.NewLine + error;
            File.AppendAllText(LogPath, line + Environment.NewLine, new UTF8Encoding(false));
        }
    }
}
