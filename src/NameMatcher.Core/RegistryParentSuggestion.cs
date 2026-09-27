namespace NameMatcher.Core;

public sealed class RegistryParentSuggestion
{
    public string SearchedName { get; init; } = "";

    public string? MatchedLegalName { get; init; }

    public string? Lei { get; init; }

    public string? ParentLegalName { get; init; }

    public string? ParentLei { get; init; }

    public string Message { get; init; } = "";
}
