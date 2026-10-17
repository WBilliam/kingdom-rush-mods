using System.Security.Cryptography;
using System.Text;
using KingdomRush.ModPatcher.Core.Compatibility;
using KingdomRush.ModPatcher.Core.Payloads;
using Xunit;

namespace KingdomRush.ModPatcher.Core.Tests;

public sealed class PayloadTests
{
    [Fact]
    public void SupportedBuildPinsTheVerifiedSteamExecutableAndOriginalMain()
    {
        SupportedBuild build = SupportedBuild.KingdomRushSteam24662480;

        Assert.Equal("24662480", build.BuildId);
        Assert.Equal("kr1-desktop-6.4.46", build.GameVersion);
        Assert.Equal("ebb4a3a9fd4fa6a30fb98e2b39fb32f0eff8630d47a3951b658339c26ce91e7b", build.ExecutableSha256);
        Assert.Equal("351ed141a4c29daf74a46e1768a90b89c6bc6bb962fe1d7b8981bb11a989dc70", build.OriginalMainSha256);
    }

    [Fact]
    public void EmbeddedPayloadsMatchOwnedSourcesAndValidate()
    {
        ModPayloadSet payloads = EmbeddedPayloadProvider.Load();

        Assert.Equal(
            "cd624da2eadfb16888c413fa4514fa66b3a6579a10dbf74c290aca5343df947b",
            Convert.ToHexString(SHA256.HashData(payloads.ReverseGold)).ToLowerInvariant());
        Assert.Equal(
            "b759626ad975bec532ae9f7f3190c617b568655e544c31bb9855176a18f093dd",
            Convert.ToHexString(SHA256.HashData(payloads.FourHero)).ToLowerInvariant());
        Assert.DoesNotContain('\r', Encoding.UTF8.GetString(payloads.ReverseGold));
        Assert.DoesNotContain('\r', Encoding.UTF8.GetString(payloads.FourHero));
        Assert.DoesNotContain('\r', Encoding.UTF8.GetString(payloads.Bootstrap));
        payloads.ValidateHashes();
    }

    [Fact]
    public void BootstrapLoadsBothModsAroundThePlayersOriginalMainInOrder()
    {
        string bootstrap = Encoding.UTF8.GetString(EmbeddedPayloadProvider.Load().Bootstrap);
        string[] markers =
        [
            "reverse_gold.lua",
            "reverse_runtime.bootstrap()",
            "multi_hero.lua",
            "multi_runtime.bootstrap()",
            "_modpack_main_orig.lua",
            "reverse_runtime.after_main()",
            "multi_runtime.after_main()",
        ];

        int previous = -1;
        foreach (string marker in markers)
        {
            int position = bootstrap.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(position > previous, $"Expected {marker} after the previous bootstrap stage.");
            previous = position;
        }
    }

    [Fact]
    public void ValidationNamesThePayloadWhoseBytesChanged()
    {
        ModPayloadSet valid = EmbeddedPayloadProvider.Load();
        byte[] changedReverseGold = [.. valid.ReverseGold];
        changedReverseGold[0] ^= 0xff;
        ModPayloadSet changed = new(changedReverseGold, valid.FourHero, valid.Bootstrap);

        InvalidDataException error = Assert.Throws<InvalidDataException>(changed.ValidateHashes);

        Assert.Contains("Reverse Gold", error.Message, StringComparison.Ordinal);
    }
}
