namespace KingdomRush.ModPatcher.Core.Transactions;

public sealed class PhysicalFileSystemCapabilities : IFileSystemCapabilities
{
    public void EnsureExclusiveWrite(string path)
    {
        using FileStream _ = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    public long GetAvailableFreeBytes(string destinationPath)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(destinationPath))
            ?? throw new IOException($"Cannot determine drive for {destinationPath}.");
        return new DriveInfo(root).AvailableFreeSpace;
    }
}
