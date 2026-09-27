namespace NameMatcher.Core;

public sealed class CompanySaveException : Exception
{
    public CompanySaveException(string message)
        : base(message)
    {
    }
}
