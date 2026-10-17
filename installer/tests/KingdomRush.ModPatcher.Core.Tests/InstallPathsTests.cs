using KingdomRush.ModPatcher.Core.Transactions;
using Xunit;

namespace KingdomRush.ModPatcher.Core.Tests;

public sealed class InstallPathsTests
{
    [Fact]
    public void UsesTheGamesActualUnderscoreSaveDirectory()
    {
        string appData = Path.Combine(Path.GetTempPath(), "appdata-root");

        InstallPaths paths = InstallPaths.ForGameDirectory(
            Path.Combine(Path.GetTempPath(), "Kingdom Rush"), appData);

        Assert.Equal(Path.Combine(Path.GetFullPath(appData), "Kingdom_rush"), paths.SaveDirectory);
    }
}
