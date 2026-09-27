namespace NameMatcher.Core;

public sealed class TokenPairResult
{
    public string? SearchWord { get; init; }

    public string? CandidateWord { get; init; }

    public int WordScore { get; init; }
}
