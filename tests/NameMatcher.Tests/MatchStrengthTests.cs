using NameMatcher.Core;

namespace NameMatcher.Tests;

public class MatchStrengthTests
{
    [Theory]
    [InlineData(100, "Strong Match")]
    [InlineData(90, "Strong Match")]
    [InlineData(89, "Possible Match")]
    [InlineData(75, "Possible Match")]
    [InlineData(74, "Weak Match")]
    [InlineData(50, "Weak Match")]
    public void Labels_follow_the_display_bands(int score, string label)
    {
        Assert.Equal(label, MatchStrength.Label(score));
    }
}
