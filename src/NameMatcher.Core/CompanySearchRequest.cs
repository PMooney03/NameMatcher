namespace NameMatcher.Core;

public sealed class CompanySearchRequest
{
    public string CompanyName { get; init; } = "";

    public int MinimumScore { get; init; } = 50;

    public int MaxResults { get; init; } = 20;
}
