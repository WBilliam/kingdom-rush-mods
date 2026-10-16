namespace KingdomRush.ModPatcher.Core.Transactions;

public interface IFileSystemCapabilities
{
    void EnsureExclusiveWrite(string path);
    long GetAvailableFreeBytes(string destinationPath);
}
