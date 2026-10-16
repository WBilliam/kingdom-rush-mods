using System.Security.Cryptography;

namespace KingdomRush.ModPatcher.Core.Payloads;

public sealed record ModPayloadSet(byte[] ReverseGold, byte[] FourHero, byte[] Bootstrap)
{
    public const string ReverseGoldSha256 = "cd624da2eadfb16888c413fa4514fa66b3a6579a10dbf74c290aca5343df947b";
    public const string FourHeroSha256 = "8dac0baf7f2f4c382ffb7d385579f2dc4ffc88e4832ecf83946377d3be8943c8";

    public void ValidateHashes()
    {
        ValidatePayload("Reverse Gold", ReverseGold, ReverseGoldSha256);
        ValidatePayload("Four-Hero", FourHero, FourHeroSha256);
        if (Bootstrap.Length == 0)
        {
            throw new InvalidDataException("Combined bootstrap payload is empty.");
        }
    }

    private static void ValidatePayload(string name, byte[] payload, string expectedHex)
    {
        byte[] actual = SHA256.HashData(payload);
        byte[] expected = Convert.FromHexString(expectedHex);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            throw new InvalidDataException($"{name} payload SHA-256 mismatch.");
        }
    }
}
