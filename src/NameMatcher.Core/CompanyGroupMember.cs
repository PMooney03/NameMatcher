namespace NameMatcher.Core;

public sealed class CompanyGroupMember
{
    public int CompanyId { get; init; }

    public string CompanyName { get; init; } = "";

    public string NormalisedName { get; init; } = "";

    public string Role { get; init; } = "";
}
