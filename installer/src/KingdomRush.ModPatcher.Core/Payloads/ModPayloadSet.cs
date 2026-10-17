using System.Security.Cryptography;

namespace KingdomRush.ModPatcher.Core.Payloads;

public sealed record ModPayloadSet(byte[] ReverseGold, byte[] FourHero, byte[] Bootstrap)
{
    public const string ReverseGoldSha256 = "cd624da2eadfb16888c413fa4514fa66b3a6579a10dbf74c290aca5343df947b";
    public const string FourHeroSha256 = "b759626ad975bec532ae9f7f3190c617b568655e544c31bb9855176a18f093dd";

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
