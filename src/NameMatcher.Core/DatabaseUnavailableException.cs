namespace NameMatcher.Core;

public sealed class DatabaseUnavailableException : Exception
{
    public DatabaseUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
