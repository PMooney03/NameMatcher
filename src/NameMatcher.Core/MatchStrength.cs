namespace NameMatcher.Core;

/// <summary>
/// Labels for the results page. These do not change the SQL score.
/// </summary>
public static class MatchStrength
{
    public static string Label(int finalScore) => finalScore switch
    {
        >= 90 => "Strong Match",
        >= 75 => "Possible Match",
        >= 50 => "Weak Match",
        _ => "Low"
    };

    public static string CssClass(int finalScore) => finalScore switch
    {
        >= 90 => "match-strong",
        >= 75 => "match-possible",
        >= 50 => "match-weak",
        _ => "match-low"
    };
}
