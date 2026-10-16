namespace KingdomRush.ModPatcher.Core.Compatibility;

public sealed record SupportedBuild(
    string BuildId,
    string GameVersion,
    string ExecutableSha256,
    string OriginalMainSha256)
{
    public static SupportedBuild KingdomRushSteam24662480 { get; } = new(
        "24662480",
        "kr1-desktop-6.4.46",
        "ebb4a3a9fd4fa6a30fb98e2b39fb32f0eff8630d47a3951b658339c26ce91e7b",
        "351ed141a4c29daf74a46e1768a90b89c6bc6bb962fe1d7b8981bb11a989dc70");
}
