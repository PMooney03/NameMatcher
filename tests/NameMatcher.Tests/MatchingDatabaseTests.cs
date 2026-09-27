using NameMatcher.Core;
using NameMatcher.Infrastructure;
using Npgsql;

namespace NameMatcher.Tests;

public class MatchingDatabaseTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=namematcher;Username=namematcher;Password=namematcher";

    [Fact]
    public async Task Expected_pairs_score_high_and_unrelated_names_do_not()
    {
        Assert.True(await CanConnectAsync(), "Start PostgreSQL with docker compose before running these tests.");

        var repository = new CompanyRepository(
            NpgsqlDataSource.Create(ConnectionString),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CompanyRepository>.Instance);

        var steven = await FindAsync(repository, "Steven Engineering Ltd", "Stephen Engineering Limited");
        var stefen = await FindAsync(repository, "Stefen Engineering", "Stephen Engineering Limited");
        var oreilly = await FindAsync(repository, "OReilly Technology Ltd", "O'Reilly Technologies Limited");
        var mac = await FindAsync(repository, "McDonnell Construction", "MacDonnell Construction");
        var mcdonald = await FindAsync(repository, "McDonnell Construction", "McDonald Construction");
        var acme = await FindAsync(repository, "ACME Software Ltd", "Acme Software Limited");

        Assert.True(steven.FinalScore >= 80);
        Assert.True(stefen.FinalScore >= 80);
        Assert.True(oreilly.FinalScore >= 80);
        Assert.True(mac.FinalScore >= 90);
        Assert.True(mcdonald.FinalScore < mac.FinalScore);
        Assert.Equal(100, acme.FinalScore);
        Assert.True(acme.IsExactNormalisedMatch);

        var zenith = await repository.FindMatchesAsync(new CompanySearchRequest
        {
            CompanyName = "Stephen Engineering Limited",
            MinimumScore = 0,
            MaxResults = 200
        });

        var unrelated = zenith.SingleOrDefault(row => row.CompanyName == "Zenith Logistics Limited");
        Assert.True(steven.TokenScore >= 70);
        Assert.True(unrelated is null || unrelated.FinalScore < 50);

        var benchmark = await repository.GetBenchmarkAsync();
        Assert.NotEmpty(benchmark);
        Assert.All(benchmark, row => Assert.True(row.Passed, row.Note));

        var comparison = await repository.CompareNamesAsync("Stefen Engineering Ltd", "Stephen Engineering Limited");
        Assert.NotNull(comparison);
        Assert.True(comparison.FinalScore >= 80);
        var pairs = await repository.ExplainTokensAsync(comparison.NormalisedLeft, comparison.NormalisedRight);
        Assert.Contains(pairs, pair => pair.SearchWord == "ENGINEERING" && pair.CandidateWord == "ENGINEERING");

        var google = await FindAsync(repository, "Google", "Google");
        var groups = await repository.GetGroupSummariesAsync([google.CompanyId]);
        Assert.Equal("Alphabet", groups[google.CompanyId].ParentName);
        Assert.Contains("YouTube", groups[google.CompanyId].GroupWith);

        var stored = await repository.AddCompanyAsync("Procedure Test Holdings Limited");
        Assert.Equal("PROCEDURE TEST HOLDINGS", stored.NormalisedName);
        Assert.True(stored.Active);

        await repository.SetCompanyActiveAsync(stored.CompanyId, active: false);
        var hidden = await repository.FindMatchesAsync(new CompanySearchRequest
        {
            CompanyName = "Procedure Test Holdings Limited",
            MinimumScore = 0,
            MaxResults = 20
        });
        Assert.DoesNotContain(hidden, row => row.CompanyId == stored.CompanyId);

        await repository.SetCompanyActiveAsync(stored.CompanyId, active: true);
        var restored = await repository.FindMatchesAsync(new CompanySearchRequest
        {
            CompanyName = "Procedure Test Holdings Limited",
            MinimumScore = 0,
            MaxResults = 20
        });
        Assert.Contains(restored, row => row.CompanyId == stored.CompanyId);

        await using (var cleanup = await NpgsqlDataSource.Create(ConnectionString).OpenConnectionAsync())
        {
            await using var command = new NpgsqlCommand("CALL usp_delete_company(@p_company_id)", cleanup);
            command.Parameters.AddWithValue("p_company_id", stored.CompanyId);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<CompanyMatchResult> FindAsync(
        CompanyRepository repository,
        string search,
        string candidate)
    {
        var results = await repository.FindMatchesAsync(new CompanySearchRequest
        {
            CompanyName = search,
            MinimumScore = 0,
            MaxResults = 200
        });

        return Assert.Single(results, row => row.CompanyName == candidate);
    }

    private static async Task<bool> CanConnectAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }
}
