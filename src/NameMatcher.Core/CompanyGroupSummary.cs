namespace NameMatcher.Core;

public sealed class CompanyGroupSummary
{
    public int CompanyId { get; init; }

    public string? ParentName { get; init; }

    public string? GroupWith { get; init; }

    public string? Line
    {
        get
        {
            var hasParent = !string.IsNullOrWhiteSpace(ParentName);
            var hasOthers = !string.IsNullOrWhiteSpace(GroupWith);

            if (hasParent && hasOthers)
            {
                return $"Part of {ParentName}, with {GroupWith}";
            }

            if (hasParent)
            {
                return $"Part of {ParentName}";
            }

            if (hasOthers)
            {
                return $"Group includes {GroupWith}";
            }

            return null;
        }
    }
}
