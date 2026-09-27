using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NameMatcher.Core;

namespace NameMatcher.Web.Pages;

public class CompareModel : PageModel
{
    private readonly ICompanyRepository _companies;
    private readonly ILogger<CompareModel> _logger;

    public CompareModel(ICompanyRepository companies, ILogger<CompareModel> logger)
    {
        _companies = companies;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? LeftName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? RightName { get; set; }

    public NameComparisonResult? Comparison { get; private set; }

    public IReadOnlyList<TokenPairResult> TokenPairs { get; private set; } = [];

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public string? DatabaseError { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (!Request.Query.ContainsKey("LeftName") && !Request.Query.ContainsKey("RightName"))
        {
            return;
        }

        var errors = new List<string>();
        AddNameError(errors, LeftName, "first");
        AddNameError(errors, RightName, "second");
        Errors = errors;

        if (Errors.Count > 0)
        {
            return;
        }

        try
        {
            Comparison = await _companies.CompareNamesAsync(LeftName!.Trim(), RightName!.Trim(), cancellationToken);
            if (Comparison is null)
            {
                Errors = ["Those names have nothing left to compare after normalisation."];
                return;
            }

            TokenPairs = await _companies.ExplainTokensAsync(
                Comparison.NormalisedLeft,
                Comparison.NormalisedRight,
                cancellationToken);
        }
        catch (DatabaseUnavailableException exception)
        {
            _logger.LogError(exception, "Name comparison could not reach the database");
            DatabaseError = "The name database is unavailable. Check that PostgreSQL is running, then try again.";
        }
    }

    private static void AddNameError(List<string> errors, string? name, string which)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add($"Enter the {which} name.");
            return;
        }

        if (name.Trim().Length > CompanySearchValidator.MaxNameLength)
        {
            errors.Add($"The {which} name must be {CompanySearchValidator.MaxNameLength} characters or fewer.");
        }
    }
}
