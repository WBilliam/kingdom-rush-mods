using System.Security.Cryptography;
using Microsoft.Win32;
using KingdomRush.ModPatcher.Core.Compatibility;
using KingdomRush.ModPatcher.Core.Transactions;

namespace KingdomRush.ModPatcher.Core.Discovery;

public sealed class GameDiscoveryService
{
    private readonly IRegistryReader _registry;
    private readonly ModPackService _modPack;
    private readonly string _appDataDirectory;
    private readonly string _defaultSteamPath;
    private readonly SupportedBuild _build;

    public GameDiscoveryService(
        IRegistryReader registry,
        ModPackService modPack,
        string appDataDirectory,
        string defaultSteamPath,
        SupportedBuild build)
    {
        _registry = registry;
        _modPack = modPack;
        _appDataDirectory = appDataDirectory;
        _defaultSteamPath = defaultSteamPath;
        _build = build;
    }

    public IReadOnlyList<string> FindCandidates()
    {
        HashSet<string> steamRoots = new(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                {
                    AddRoot(steamRoots, _registry.ReadString(hive, view, @"SOFTWARE\Valve\Steam", "SteamPath"));
                    AddRoot(steamRoots, _registry.ReadString(hive, view, @"SOFTWARE\Valve\Steam", "InstallPath"));
                }
            }
        }
        AddRoot(steamRoots, _defaultSteamPath);

        HashSet<string> libraries = new(steamRoots, StringComparer.OrdinalIgnoreCase);
        foreach (string steamRoot in steamRoots.ToArray())
        {
            string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            try
            {
                if (File.Exists(vdf))
                {
                    foreach (string library in SteamVdfParser.ParseLibraryPaths(File.ReadAllText(vdf)))
                    {
                        libraries.Add(library);
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // An inaccessible Steam library is skipped; manual Browse remains available.
            }
        }
        return libraries
            .Select(root => Path.Combine(root, "steamapps", "common", "Kingdom Rush"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<InstallationStatus> InspectAsync(string gameDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InstallPaths paths = InstallPaths.ForGameDirectory(gameDirectory, _appDataDirectory);
        if (!File.Exists(paths.ExecutablePath))
        {
            return new InstallationStatus(InstallationState.Missing, "Kingdom Rush.exe was not found in this folder.");
        }
        if (File.Exists(paths.PendingManifestPath))
        {
            return new InstallationStatus(InstallationState.RecoveryRequired, "An interrupted installation needs recovery.");
        }
        if (File.Exists(paths.ManifestPath))
        {
            try
            {
                await _modPack.VerifyAsync(paths, cancellationToken);
                return new InstallationStatus(InstallationState.InstalledHealthy, "Reverse Gold and Four-Hero are installed and verified.");
            }
            catch (Exception error)
            {
                return new InstallationStatus(InstallationState.InstalledCorrupt, "The installed mod pack failed verification.", error.ToString());
            }
        }
        string actual = HashFile(paths.ExecutablePath);
        if (string.Equals(actual, _build.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new InstallationStatus(InstallationState.ReadyToInstall, "Supported clean Kingdom Rush build detected.");
        }
        return new InstallationStatus(
            InstallationState.Unsupported,
            "This executable is unsupported or already modified.",
            $"Detected SHA-256: {actual}");
    }

    private static void AddRoot(HashSet<string> roots, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try { roots.Add(Path.GetFullPath(value)); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException) { }
    }

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
