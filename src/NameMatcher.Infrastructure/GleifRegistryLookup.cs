using System.Net.Http.Json;
using System.Text.Json;
using NameMatcher.Core;

namespace NameMatcher.Infrastructure;

public sealed class GleifRegistryLookup : IRegistryLookup
{
    private static readonly string[] LegalSuffixes = ["INC", "LTD", "LIMITED", "LLC", "PLC", "CO", "COMPANY"];

    private readonly HttpClient _http;

    public GleifRegistryLookup(HttpClient http)
    {
        _http = http;
    }

    public async Task<RegistryParentSuggestion> FindParentAsync(
        string companyName,
        CancellationToken cancellationToken = default)
    {
        var searched = companyName.Trim();
        if (searched.Length == 0)
        {
            return new RegistryParentSuggestion
            {
                Message = "Enter a company name to look up."
            };
        }

        try
        {
            var searchUrl =
                "api/v1/lei-records?page[size]=15&filter[entity.legalName]="
                + Uri.EscapeDataString(searched);
            using var searchDoc = await _http.GetFromJsonAsync<JsonDocument>(searchUrl, cancellationToken);
            var records = searchDoc?.RootElement.GetProperty("data").EnumerateArray().ToList() ?? [];
            if (records.Count == 0)
            {
                return new RegistryParentSuggestion
                {
                    SearchedName = searched,
                    Message = "GLEIF has no record for that name. Set the parent manually."
                };
            }

            var match = records
                .Select(record => new Candidate(record, Score(record, searched)))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.LegalName.Length)
                .First();

            var parentLink = ParentLink(match.Record);
            if (parentLink is null)
            {
                return new RegistryParentSuggestion
                {
                    SearchedName = searched,
                    MatchedLegalName = match.LegalName,
                    Lei = match.Lei,
                    Message = "GLEIF lists this company and no parent. It may be the top of the group. You can still set a parent manually."
                };
            }

            using var parentDoc = await _http.GetFromJsonAsync<JsonDocument>(parentLink, cancellationToken);
            var parent = parentDoc!.RootElement.GetProperty("data");
            var parentName = parent.GetProperty("attributes").GetProperty("entity").GetProperty("legalName").GetProperty("name").GetString();

            return new RegistryParentSuggestion
            {
                SearchedName = searched,
                MatchedLegalName = match.LegalName,
                Lei = match.Lei,
                ParentLegalName = parentName,
                ParentLei = parent.GetProperty("id").GetString(),
                Message = "GLEIF suggests this parent. Save it only if it is the company you mean."
            };
        }
        catch (HttpRequestException)
        {
            return new RegistryParentSuggestion
            {
                SearchedName = searched,
                Message = "GLEIF could not be reached. Set the parent manually."
            };
        }
        catch (JsonException)
        {
            return new RegistryParentSuggestion
            {
                SearchedName = searched,
                Message = "GLEIF returned a result this page could not read. Set the parent manually."
            };
        }
        catch (TaskCanceledException)
        {
            return new RegistryParentSuggestion
            {
                SearchedName = searched,
                Message = "GLEIF took too long to answer. Set the parent manually."
            };
        }
    }

    private static int Score(JsonElement record, string searched)
    {
        var legalName = LegalName(record);
        var score = 0;
        if (RegistrationStatus(record) == "ISSUED")
        {
            score += 5;
        }

        if (SignificantName(legalName) == SignificantName(searched))
        {
            score += 50;
        }
        else if (legalName.StartsWith(searched, StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        if (ParentLink(record) is not null)
        {
            score += 3;
        }

        return score;
    }

    private static string? ParentLink(JsonElement record)
    {
        if (!record.TryGetProperty("relationships", out var relationships)
            || !relationships.TryGetProperty("direct-parent", out var parent)
            || !parent.TryGetProperty("links", out var links)
            || !links.TryGetProperty("lei-record", out var leiRecord))
        {
            return null;
        }

        var url = leiRecord.GetString();
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return url.Replace("https://api.gleif.org/", "", StringComparison.OrdinalIgnoreCase);
    }

    private static string LegalName(JsonElement record) =>
        record.GetProperty("attributes").GetProperty("entity").GetProperty("legalName").GetProperty("name").GetString()
        ?? "";

    private static string RegistrationStatus(JsonElement record) =>
        record.GetProperty("attributes").GetProperty("registration").GetProperty("status").GetString() ?? "";

    private static string SignificantName(string value)
    {
        var words = new string(value.ToUpperInvariant().Select(character => char.IsLetterOrDigit(character) ? character : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !LegalSuffixes.Contains(word));
        return string.Join(' ', words);
    }

    private sealed record Candidate(JsonElement Record, int Score)
    {
        public string LegalName { get; } = GleifRegistryLookup.LegalName(Record);

        public string Lei { get; } = Record.GetProperty("id").GetString() ?? "";
    }
}
