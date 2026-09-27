namespace NameMatcher.Core;

public static class CompanySearchValidator
{
    public const int MaxNameLength = 300;

    public static IReadOnlyList<string> Validate(string? companyName, int minimumScore)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(companyName))
        {
            errors.Add("Enter a company name to search.");
        }
        else if (companyName.Trim().Length > MaxNameLength)
        {
            errors.Add($"Company name must be {MaxNameLength} characters or fewer.");
        }

        if (minimumScore is < 0 or > 100)
        {
            errors.Add("Minimum score must be between 0 and 100.");
        }

        return errors;
    }
}
