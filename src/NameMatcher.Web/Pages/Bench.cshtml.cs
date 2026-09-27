using Microsoft.AspNetCore.Mvc.RazorPages;
using NameMatcher.Core;

namespace NameMatcher.Web.Pages;

public class BenchModel : PageModel
{
    private readonly ICompanyRepository _companies;
    private readonly ILogger<BenchModel> _logger;

    public BenchModel(ICompanyRepository companies, ILogger<BenchModel> logger)
    {
        _companies = companies;
        _logger = logger;
    }

    public IReadOnlyList<MatchBenchmarkResult> Results { get; private set; } = [];

    public string? DatabaseError { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            Results = await _companies.GetBenchmarkAsync(cancellationToken);
        }
        catch (DatabaseUnavailableException exception)
        {
            _logger.LogError(exception, "Benchmark could not reach the database");
            DatabaseError = "The name database is unavailable. Check that PostgreSQL is running, then try again.";
        }
    }
}
