namespace NameMatcher.Core;

public interface ICompanyRepository
{
    Task<IReadOnlyList<CompanyMatchResult>> FindMatchesAsync(
        CompanySearchRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, CompanyGroupSummary>> GetGroupSummariesAsync(
        IReadOnlyList<int> companyIds,
        CancellationToken cancellationToken = default);

    Task<CompanyMatchResult?> ExplainMatchAsync(
        string companyName,
        int companyId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MatchBenchmarkResult>> GetBenchmarkAsync(
        CancellationToken cancellationToken = default);

    Task<NameComparisonResult?> CompareNamesAsync(
        string leftName,
        string rightName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TokenPairResult>> ExplainTokensAsync(
        string normalisedLeft,
        string normalisedRight,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredCompany>> ListStoredCompaniesAsync(
        CancellationToken cancellationToken = default);

    Task<StoredCompany> AddCompanyAsync(
        string companyName,
        CancellationToken cancellationToken = default);

    Task SetCompanyActiveAsync(
        int companyId,
        bool active,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompanyGroupMember>> GetCompanyGroupAsync(
        string companyName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompanyWorkLink>> GetCompanyLinksAsync(
        string companyName,
        CancellationToken cancellationToken = default);

    Task SetParentCompanyAsync(
        string companyName,
        string parentName,
        CancellationToken cancellationToken = default);

    Task AddCompanyLinkAsync(
        string companyName,
        string partnerName,
        string linkType,
        string? note,
        CancellationToken cancellationToken = default);

    Task EnsureStoredCompanyAsync(
        string companyName,
        CancellationToken cancellationToken = default);
}
