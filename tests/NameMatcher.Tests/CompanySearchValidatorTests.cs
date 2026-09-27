using NameMatcher.Core;

namespace NameMatcher.Tests;

public class CompanySearchValidatorTests
{
    [Fact]
    public void Rejects_empty_name()
    {
        var errors = CompanySearchValidator.Validate("   ", 50);

        Assert.Contains("Enter a company name to search.", errors);
    }

    [Fact]
    public void Rejects_score_outside_0_to_100()
    {
        var errors = CompanySearchValidator.Validate("Acme", 120);

        Assert.Contains("Minimum score must be between 0 and 100.", errors);
    }

    [Fact]
    public void Accepts_a_normal_search()
    {
        var errors = CompanySearchValidator.Validate("Stephen Engineering", 50);

        Assert.Empty(errors);
    }
}
