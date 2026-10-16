namespace KingdomRush.ModPatcher.Core.Archives;

public sealed record PatchImageResult(
    byte[] Image,
    string OriginalSha256,
    string PatchedSha256,
    IReadOnlyDictionary<string, string> EntrySha256);
