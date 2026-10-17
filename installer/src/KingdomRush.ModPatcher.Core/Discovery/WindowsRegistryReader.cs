using Microsoft.Win32;

namespace KingdomRush.ModPatcher.Core.Discovery;

public sealed class WindowsRegistryReader : IRegistryReader
{
    public string? ReadString(RegistryHive hive, RegistryView view, string subKey, string valueName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return null;
        }
    }
}
