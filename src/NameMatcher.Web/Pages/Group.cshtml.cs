using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NameMatcher.Core;

namespace NameMatcher.Web.Pages;

public class GroupModel : PageModel
{
    private readonly ICompanyRepository _companies;
    private readonly IRegistryLookup _registry;
    private readonly ILogger<GroupModel> _logger;

    public GroupModel(ICompanyRepository companies, IRegistryLookup registry, ILogger<GroupModel> logger)
    {
        _companies = companies;
        _registry = registry;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? CompanyName { get; set; }

    public bool Searched { get; private set; }

    public string? Message { get; private set; }

    public string? DatabaseError { get; private set; }

    public IReadOnlyList<CompanyGroupMember> Group { get; private set; } = [];

    public IReadOnlyList<CompanyWorkLink> Links { get; private set; } = [];

    public RegistryParentSuggestion? Suggestion { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CompanyName))
        {
            return;
        }

        Searched = true;
        try
        {
            Group = await _companies.GetCompanyGroupAsync(CompanyName, cancellationToken);
            Links = await _companies.GetCompanyLinksAsync(CompanyName, cancellationToken);
        }
        catch (CompanySaveException exception)
        {
            Message = exception.Message;
        }
        catch (DatabaseUnavailableException exception)
        {
            _logger.LogError(exception, "Group lookup failed");
            DatabaseError = "The name database is unavailable. Check that PostgreSQL is running, then try again.";
        }
    }

    public async Task<IActionResult> OnGetRegistryAsync(string? companyName, CancellationToken cancellationToken)
    {
        CompanyName = companyName;
        if (string.IsNullOrWhiteSpace(companyName))
        {
            Suggestion = new RegistryParentSuggestion { Message = "Enter a company name to look up." };
            return Page();
        }

        Suggestion = await _registry.FindParentAsync(companyName, cancellationToken);
        await OnGetAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostRegistryAsync(
        string? childName,
        string? parentName,
        CancellationToken cancellationToken)
    {
        return await SaveAsync(
            async () =>
            {
                await _companies.EnsureStoredCompanyAsync(childName ?? "", cancellationToken);
                await _companies.EnsureStoredCompanyAsync(parentName ?? "", cancellationToken);
                await _companies.SetParentCompanyAsync(childName ?? "", parentName ?? "", cancellationToken);
            },
            childName,
            cancellationToken);
    }

    public async Task<IActionResult> OnPostParentAsync(string? childName, string? parentName, CancellationToken cancellationToken)
    {
        return await SaveAsync(
            () => _companies.SetParentCompanyAsync(childName ?? "", parentName ?? "", cancellationToken),
            childName,
            cancellationToken);
    }

    public async Task<IActionResult> OnPostLinkAsync(
        string? leftName,
        string? rightName,
        string? linkType,
        string? note,
        CancellationToken cancellationToken)
    {
        return await SaveAsync(
            () => _companies.AddCompanyLinkAsync(leftName ?? "", rightName ?? "", linkType ?? "", note, cancellationToken),
            leftName,
            cancellationToken);
    }

    private async Task<IActionResult> SaveAsync(
        Func<Task> save,
        string? showName,
        CancellationToken cancellationToken)
    {
        try
        {
            await save();
            return RedirectToPage(new { CompanyName = showName });
        }
        catch (CompanySaveException exception)
        {
            Message = exception.Message;
            CompanyName = showName;
            await OnGetAsync(cancellationToken);
            return Page();
        }
        catch (DatabaseUnavailableException exception)
        {
            _logger.LogError(exception, "Saving a company relationship failed");
            DatabaseError = "The name database is unavailable. Check that PostgreSQL is running, then try again.";
            return Page();
        }
    }
}
