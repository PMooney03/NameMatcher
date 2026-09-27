namespace NameMatcher.Core;

public interface IRegistryLookup
{
    Task<RegistryParentSuggestion> FindParentAsync(
        string companyName,
        CancellationToken cancellationToken = default);
}
