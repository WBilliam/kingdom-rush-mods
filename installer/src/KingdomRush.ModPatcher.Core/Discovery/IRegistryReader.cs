using Microsoft.Win32;

namespace KingdomRush.ModPatcher.Core.Discovery;

public interface IRegistryReader
{
    string? ReadString(RegistryHive hive, RegistryView view, string subKey, string valueName);
}
