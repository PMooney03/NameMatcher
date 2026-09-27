namespace NameMatcher.Core;

public sealed class MatchBenchmarkResult
{
    public int CaseId { get; init; }

    public string SearchName { get; init; } = "";

    public string ExpectedName { get; init; } = "";

    public string Note { get; init; } = "";

    public int? ActualScore { get; init; }

    public int? RivalScore { get; init; }

    public bool Passed { get; init; }
}
