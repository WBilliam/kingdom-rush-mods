namespace KingdomRush.ModPatcher.Core.Errors;

public sealed class CompatibilityException : PatcherException
{
    public CompatibilityException(string message) : base(message)
    {
    }

    public CompatibilityException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
