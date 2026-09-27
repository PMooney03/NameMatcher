using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NameMatcher.Core;

namespace NameMatcher.Web.Pages;

public class MatchModel : PageModel
{
    private readonly ICompanyRepository _companies;
    private readonly ILogger<MatchModel> _logger;

    public MatchModel(ICompanyRepository companies, ILogger<MatchModel> logger)
    {
        _companies = companies;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? CompanyName { get; set; }

    [BindProperty(SupportsGet = true)]
    public int CompanyId { get; set; }

    public CompanyMatchResult? Match { get; private set; }

    public IReadOnlyList<TokenPairResult> TokenPairs { get; private set; } = [];

    public CompanyGroupSummary? Group { get; private set; }

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public string? DatabaseError { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Errors = CompanySearchValidator.Validate(CompanyName, minimumScore: 0);
        if (CompanyId <= 0)
        {
            Errors = [.. Errors, "Choose a result to inspect."];
        }

        if (Errors.Count > 0)
        {
            return;
        }

        try
        {
            Match = await _companies.ExplainMatchAsync(CompanyName!.Trim(), CompanyId, cancellationToken);
            if (Match is null)
            {
                Errors = ["That company was not found."];
                return;
            }

            TokenPairs = await _companies.ExplainTokensAsync(
                Match.NormalisedSearch,
                Match.NormalisedName,
                cancellationToken);
            var groups = await _companies.GetGroupSummariesAsync([Match.CompanyId], cancellationToken);
            groups.TryGetValue(Match.CompanyId, out var group);
            Group = group;
        }
        catch (DatabaseUnavailableException exception)
        {
            _logger.LogError(exception, "Match detail could not reach the database");
            DatabaseError = "The name database is unavailable. Check that PostgreSQL is running, then try again.";
        }
    }
}
