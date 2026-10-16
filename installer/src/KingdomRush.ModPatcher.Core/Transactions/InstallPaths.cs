namespace KingdomRush.ModPatcher.Core.Transactions;

public sealed record InstallPaths(
    string GameDirectory,
    string ExecutablePath,
    string SaveDirectory,
    string ManifestPath,
    string PendingManifestPath)
{
    public static InstallPaths ForGameDirectory(string gameDirectory, string appDataDirectory)
    {
        string game = Path.GetFullPath(gameDirectory);
        string saves = Path.Combine(Path.GetFullPath(appDataDirectory), "Kingdom Rush");
        return new InstallPaths(
            game,
            Path.Combine(game, "Kingdom Rush.exe"),
            saves,
            Path.Combine(game, "kingdom_rush_modpack_manifest.json"),
            Path.Combine(game, "kingdom_rush_modpack_manifest.pending.json"));
    }
}
