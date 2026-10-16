namespace KingdomRush.ModPatcher.Core.Transactions;

public sealed record OperationProgress(string Stage, string Message, bool IsCommitCritical = false);
