using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NameMatcher.Core;

namespace NameMatcher.Web.Pages;

public class IndexModel : PageModel
{
    private readonly ICompanyRepository _companies;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(ICompanyRepository companies, ILogger<IndexModel> logger)
    {
        _companies = companies;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? CompanyName { get; set; }

    [BindProperty(SupportsGet = true)]
    public int MinimumScore { get; set; } = 50;

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public string? DatabaseError { get; private set; }

    public IReadOnlyList<CompanyMatchResult> Results { get; private set; } = [];

    public IReadOnlyDictionary<int, CompanyGroupSummary> Groups { get; private set; } =
        new Dictionary<int, CompanyGroupSummary>();

    public bool SearchSubmitted { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (!Request.Query.ContainsKey("CompanyName"))
        {
            return;
        }

        SearchSubmitted = true;
        Errors = CompanySearchValidator.Validate(CompanyName, MinimumScore);
        if (Errors.Count > 0)
        {
            return;
        }

        try
        {
            Results = await _companies.FindMatchesAsync(
                new CompanySearchRequest
                {
                    CompanyName = CompanyName!.Trim(),
                    MinimumScore = MinimumScore
                },
                cancellationToken);
            Groups = await _companies.GetGroupSummariesAsync(
                Results.Select(result => result.CompanyId).ToList(),
                cancellationToken);
        }
        catch (DatabaseUnavailableException exception)
        {
            _logger.LogError(exception, "Search could not reach the database");
            DatabaseError = "The name database is unavailable. Check that PostgreSQL is running, then try again.";
        }
    }
}
