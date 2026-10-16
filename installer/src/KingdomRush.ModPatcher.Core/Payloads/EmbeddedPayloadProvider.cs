using System.Reflection;
using System.Text;

namespace KingdomRush.ModPatcher.Core.Payloads;

public static class EmbeddedPayloadProvider
{
    private const string ResourcePrefix = "KingdomRush.ModPatcher.Payloads.";

    public static ModPayloadSet Load()
    {
        Assembly assembly = typeof(EmbeddedPayloadProvider).Assembly;
        ModPayloadSet payloads = new(
            ReadLuaResource(assembly, ResourcePrefix + "reverse_gold.lua"),
            ReadLuaResource(assembly, ResourcePrefix + "multi_hero.lua"),
            ReadLuaResource(assembly, ResourcePrefix + "combined_main.lua"));
        payloads.ValidateHashes();
        return payloads;
    }

    private static byte[] ReadLuaResource(Assembly assembly, string name)
    {
        using Stream stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"Embedded payload is missing: {name}");
        using StreamReader reader = new(
            stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true);
        string canonical = reader.ReadToEnd().Replace("\r\n", "\n").Replace('\r', '\n');
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(canonical);
    }
}
