namespace NameMatcher.Core;

public sealed class CompanyMatchResult
{
    public int CompanyId { get; init; }

    public string CompanyName { get; init; } = "";

    public string NormalisedName { get; init; } = "";

    public string NormalisedSearch { get; init; } = "";

    public int ExactMatchScore { get; init; }

    public int SoundexScore { get; init; }

    public int DifferenceScore { get; init; }

    public int LevenshteinDistance { get; init; }

    public int LevenshteinScore { get; init; }

    public int TokenScore { get; init; }

    public int TrigramScore { get; init; }

    public int FinalScore { get; init; }

    public bool IsExactNormalisedMatch { get; init; }

    public bool IsPhoneticMatch { get; init; }
}
