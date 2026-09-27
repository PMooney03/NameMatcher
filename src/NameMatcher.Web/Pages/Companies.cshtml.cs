using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NameMatcher.Core;

namespace NameMatcher.Web.Pages;

public class CompaniesModel : PageModel
{
    private readonly ICompanyRepository _companies;
    private readonly ILogger<CompaniesModel> _logger;

    public CompaniesModel(ICompanyRepository companies, ILogger<CompaniesModel> logger)
    {
        _companies = companies;
        _logger = logger;
    }

    [BindProperty]
    public string? CompanyName { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? AddedId { get; set; }

    public IReadOnlyList<StoredCompany> Companies { get; private set; } = [];

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public string? DatabaseError { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(CompanyName))
        {
            errors.Add("Enter a company name.");
        }
        else if (CompanyName.Trim().Length > CompanySearchValidator.MaxNameLength)
        {
            errors.Add($"Company name must be {CompanySearchValidator.MaxNameLength} characters or fewer.");
        }
        if (errors.Count > 0)
        {
            Errors = errors;
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            var stored = await _companies.AddCompanyAsync(CompanyName!.Trim(), cancellationToken);
            return RedirectToPage(new { AddedId = stored.CompanyId });
        }
        catch (CompanySaveException exception)
        {
            Errors = [exception.Message];
            await LoadAsync(cancellationToken);
            return Page();
        }
        catch (DatabaseUnavailableException exception)
        {
            _logger.LogError(exception, "Add company could not reach the database");
            DatabaseError = "The name database is unavailable. Check that PostgreSQL is running, then try again.";
            return Page();
        }
    }

    public async Task<IActionResult> OnPostActiveAsync(int companyId, bool active, CancellationToken cancellationToken)
    {
        try
        {
            await _companies.SetCompanyActiveAsync(companyId, active, cancellationToken);
            return RedirectToPage(new { AddedId });
        }
        catch (CompanySaveException exception)
        {
            Errors = [exception.Message];
            await LoadAsync(cancellationToken);
            return Page();
        }
        catch (DatabaseUnavailableException exception)
        {
            _logger.LogError(exception, "Changing company visibility failed");
            DatabaseError = "The name database is unavailable. Check that PostgreSQL is running, then try again.";
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Companies = await _companies.ListStoredCompaniesAsync(cancellationToken);
        }
        catch (DatabaseUnavailableException exception)
        {
            _logger.LogError(exception, "Stored company list could not reach the database");
            DatabaseError = "The name database is unavailable. Check that PostgreSQL is running, then try again.";
        }
    }
}
