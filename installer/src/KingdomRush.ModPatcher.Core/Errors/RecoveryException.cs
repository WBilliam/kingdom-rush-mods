namespace KingdomRush.ModPatcher.Core.Errors;

public sealed class RecoveryException : PatcherException
{
    public RecoveryException(string message) : base(message) { }
    public RecoveryException(string message, Exception innerException) : base(message, innerException) { }
}
