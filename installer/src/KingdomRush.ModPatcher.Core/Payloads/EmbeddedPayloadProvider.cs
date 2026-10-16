using System.Reflection;

namespace KingdomRush.ModPatcher.Core.Payloads;

public static class EmbeddedPayloadProvider
{
    private const string ResourcePrefix = "KingdomRush.ModPatcher.Payloads.";

    public static ModPayloadSet Load()
    {
        Assembly assembly = typeof(EmbeddedPayloadProvider).Assembly;
        ModPayloadSet payloads = new(
            ReadResource(assembly, ResourcePrefix + "reverse_gold.lua"),
            ReadResource(assembly, ResourcePrefix + "multi_hero.lua"),
            ReadResource(assembly, ResourcePrefix + "combined_main.lua"));
        payloads.ValidateHashes();
        return payloads;
    }

    private static byte[] ReadResource(Assembly assembly, string name)
    {
        using Stream stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"Embedded payload is missing: {name}");
        using MemoryStream output = new();
        stream.CopyTo(output);
        return output.ToArray();
    }
}
