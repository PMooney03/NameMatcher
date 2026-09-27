namespace NameMatcher.Core;

public sealed class StoredCompany
{
    public int CompanyId { get; init; }

    public string CompanyName { get; init; } = "";

    public string NormalisedName { get; init; } = "";

    public bool Active { get; init; } = true;

    public DateTimeOffset CreatedAt { get; init; }
}
