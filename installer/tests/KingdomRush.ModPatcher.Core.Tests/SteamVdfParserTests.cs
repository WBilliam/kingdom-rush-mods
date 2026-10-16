using KingdomRush.ModPatcher.Core.Discovery;
using Xunit;

namespace KingdomRush.ModPatcher.Core.Tests;

public sealed class SteamVdfParserTests
{
    [Fact]
    public void ParsesMultipleEscapedLibraryPathsAndDeduplicatesThem()
    {
        const string vdf = "\"libraryfolders\" { \"0\" { \"path\" \"C:\\\\Steam\" } \"1\" { \"path\" \"D:\\\\Games\" } \"2\" { \"path\" \"d:\\\\games\" } }";

        IReadOnlyList<string> paths = SteamVdfParser.ParseLibraryPaths(vdf);

        Assert.Equal([@"C:\Steam", @"D:\Games"], paths);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"libraryfolders\" { \"0\" { \"path\" \"unterminated }")]
    [InlineData("\"libraryfolders\" { \"0\" { \"other\" \"value\" } }")]
    public void MalformedOrPathlessVdfReturnsNoLibraries(string vdf)
    {
        Assert.Empty(SteamVdfParser.ParseLibraryPaths(vdf));
    }
}
