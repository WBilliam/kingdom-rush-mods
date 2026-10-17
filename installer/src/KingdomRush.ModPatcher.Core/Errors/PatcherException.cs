namespace KingdomRush.ModPatcher.Core.Errors;

public class PatcherException : Exception
{
    public PatcherException(string message) : base(message)
    {
    }

    public PatcherException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
