namespace NameMatcher.Core;

public sealed class CompanyWorkLink
{
    public string CompanyName { get; init; } = "";

    public string PartnerName { get; init; } = "";

    public string LinkType { get; init; } = "";

    public string? Note { get; init; }
}
