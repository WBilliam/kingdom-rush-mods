using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using KingdomRush.ModPatcher.Core.Archives;
using KingdomRush.ModPatcher.Core.Compatibility;
using KingdomRush.ModPatcher.Core.Errors;
using KingdomRush.ModPatcher.Core.Payloads;
using Xunit;

namespace KingdomRush.ModPatcher.Core.Tests;

public sealed class PatchImageBuilderTests
{
    private static readonly byte[] OriginalMain = SyntheticFusedImage.Utf8("return { game = 'original' }\n");
    private static readonly byte[] Unrelated = [0, 1, 2, 3, 4, 255];

    [Fact]
    public void BuildPreservesPrefixAndUnrelatedEntriesAndInstallsBothMods()
    {
        byte[] original = CleanImage();
        ModPayloadSet payloads = EmbeddedPayloadProvider.Load();

        PatchImageResult result = PatchImageBuilder.Build(original, BuildFor(original), payloads);

        Assert.Equal(SyntheticFusedImage.Prefix, result.Image[..SyntheticFusedImage.Prefix.Length]);
        IReadOnlyDictionary<string, byte[]> entries = ReadEntries(result.Image);
        Assert.Equal(Unrelated, entries["assets/sentinel.bin"]);
        Assert.Equal(OriginalMain, entries["_modpack_main_orig.lua"]);
        Assert.Equal(payloads.Bootstrap, entries["main.lua"]);
        Assert.Equal(payloads.ReverseGold, entries["reverse_gold.lua"]);
        Assert.Equal(payloads.FourHero, entries["multi_hero.lua"]);
        Assert.Equal(Sha256(original), result.OriginalSha256);
        Assert.Equal(Sha256(result.Image), result.PatchedSha256);
        Assert.Equal(ModPayloadSet.ReverseGoldSha256, result.EntrySha256["reverse_gold.lua"]);
        Assert.Equal(ModPayloadSet.FourHeroSha256, result.EntrySha256["multi_hero.lua"]);
    }

    [Fact]
    public void BuildPreservesExactBootstrapOrder()
    {
        byte[] original = CleanImage();

        PatchImageResult result = PatchImageBuilder.Build(original, BuildFor(original), EmbeddedPayloadProvider.Load());

        string wrapper = Encoding.UTF8.GetString(ReadEntries(result.Image)["main.lua"]);
        string[] markers =
        [
            "reverse_gold.lua", "reverse_runtime.bootstrap()", "multi_hero.lua",
            "multi_runtime.bootstrap()", "_modpack_main_orig.lua",
            "reverse_runtime.after_main()", "multi_runtime.after_main()",
        ];
        int prior = -1;
        foreach (string marker in markers)
        {
            int current = wrapper.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(current > prior, $"{marker} was not after the prior stage.");
            prior = current;
        }
    }

    [Fact]
    public void BuildRejectsWrongExecutableHash()
    {
        byte[] original = CleanImage();
        SupportedBuild wrong = BuildFor(original) with { ExecutableSha256 = new string('0', 64) };

        CompatibilityException error = Assert.Throws<CompatibilityException>(
            () => PatchImageBuilder.Build(original, wrong, EmbeddedPayloadProvider.Load()));

        Assert.Contains("executable SHA-256", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRejectsWrongOriginalMainHash()
    {
        byte[] original = CleanImage();
        SupportedBuild wrong = BuildFor(original) with { OriginalMainSha256 = new string('0', 64) };

        CompatibilityException error = Assert.Throws<CompatibilityException>(
            () => PatchImageBuilder.Build(original, wrong, EmbeddedPayloadProvider.Load()));

        Assert.Contains("main.lua SHA-256", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("main.lua", "main.lua")]
    [InlineData("main.lua", "MAIN.LUA")]
    public void ReadRejectsDuplicateAndCaseShadowedNames(string first, string second)
    {
        byte[] image = SyntheticFusedImage.Create(
            (first, OriginalMain),
            (second, OriginalMain));

        CompatibilityException error = Assert.Throws<CompatibilityException>(() => FusedArchive.Read(image));

        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRejectsExistingReservedOriginalEntry()
    {
        byte[] image = SyntheticFusedImage.Create(
            ("main.lua", OriginalMain),
            ("_modpack_main_orig.lua", OriginalMain));

        CompatibilityException error = Assert.Throws<CompatibilityException>(
            () => PatchImageBuilder.Build(image, BuildFor(image), EmbeddedPayloadProvider.Load()));

        Assert.Contains("_modpack_main_orig.lua", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseUsesValidatedEndRecordsInsteadOfFakePrefixSignature()
    {
        byte[] image = CleanImage();

        ZipLayout layout = ZipLayout.Parse(image);

        Assert.Equal(SyntheticFusedImage.Prefix.Length, layout.PrefixLength);
    }

    [Fact]
    public void ParseRejectsTruncatedEndRecord()
    {
        byte[] image = CleanImage();

        CompatibilityException error = Assert.Throws<CompatibilityException>(
            () => ZipLayout.Parse(image[..^5]));

        Assert.Contains("end of central directory", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadRejectsCorruptEntryData()
    {
        byte[] image = CleanImage();
        ZipLayout layout = ZipLayout.Parse(image);
        int finalCompressedByte = layout.PrefixLength + layout.CentralDirectoryOffset - 1;
        image[finalCompressedByte] ^= 0xff;

        Assert.Throws<PatcherException>(() => FusedArchive.Read(image));
    }

    [Fact]
    public void ReadRejectsUnsafeEntryNames()
    {
        byte[] image = SyntheticFusedImage.Create(
            ("main.lua", OriginalMain),
            ("../outside.lua", Unrelated));

        CompatibilityException error = Assert.Throws<CompatibilityException>(() => FusedArchive.Read(image));

        Assert.Contains("unsafe", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] CleanImage() => SyntheticFusedImage.Create(
        ("main.lua", OriginalMain),
        ("assets/sentinel.bin", Unrelated));

    private static SupportedBuild BuildFor(byte[] image) => new(
        "test-build", "test-version", Sha256(image), Sha256(OriginalMain));

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static IReadOnlyDictionary<string, byte[]> ReadEntries(byte[] image)
    {
        ZipLayout layout = ZipLayout.Parse(image);
        using MemoryStream stream = new(image, layout.PrefixLength, image.Length - layout.PrefixLength, writable: false);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(
            entry => entry.FullName,
            entry =>
            {
                using Stream source = entry.Open();
                using MemoryStream output = new();
                source.CopyTo(output);
                return output.ToArray();
            },
            StringComparer.Ordinal);
    }
}
