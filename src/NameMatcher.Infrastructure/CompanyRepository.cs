using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using NameMatcher.Core;
using Npgsql;
using NpgsqlTypes;

namespace NameMatcher.Infrastructure;

public sealed class CompanyRepository : ICompanyRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<CompanyRepository> _logger;

    static CompanyRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public CompanyRepository(NpgsqlDataSource dataSource, ILogger<CompanyRepository> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CompanyMatchResult>> FindMatchesAsync(
        CompanySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT *
            FROM find_company_matches(@CompanyName, @MinimumScore, @MaxResults)
            """;

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            var rows = await connection.QueryAsync<CompanyMatchResult>(
                new CommandDefinition(sql, request, cancellationToken: cancellationToken));
            return rows.ToList();
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Company search failed for {CompanyName}", request.CompanyName);
            throw new DatabaseUnavailableException(
                "The name database could not be queried.",
                exception);
        }
    }

    public async Task<IReadOnlyDictionary<int, CompanyGroupSummary>> GetGroupSummariesAsync(
        IReadOnlyList<int> companyIds,
        CancellationToken cancellationToken = default)
    {
        if (companyIds.Count == 0)
        {
            return new Dictionary<int, CompanyGroupSummary>();
        }

        const string sql = """
            SELECT s.company_id, s.parent_name, s.group_with
            FROM unnest(@CompanyIds) AS ids(company_id)
            CROSS JOIN LATERAL fn_group_summary(ids.company_id) AS s
            """;

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            var rows = await connection.QueryAsync<CompanyGroupSummary>(
                new CommandDefinition(sql, new { CompanyIds = companyIds.ToArray() }, cancellationToken: cancellationToken));
            return rows.ToDictionary(row => row.CompanyId);
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Company group summary failed");
            throw new DatabaseUnavailableException(
                "The name database could not be queried.",
                exception);
        }
    }

    public async Task<CompanyMatchResult?> ExplainMatchAsync(
        string companyName,
        int companyId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT *
            FROM explain_company_match(@CompanyName, @CompanyId)
            """;

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            return await connection.QuerySingleOrDefaultAsync<CompanyMatchResult>(
                new CommandDefinition(
                    sql,
                    new { CompanyName = companyName, CompanyId = companyId },
                    cancellationToken: cancellationToken));
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Match explanation failed for company {CompanyId}", companyId);
            throw new DatabaseUnavailableException(
                "The name database could not be queried.",
                exception);
        }
    }

    public async Task<IReadOnlyList<MatchBenchmarkResult>> GetBenchmarkAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT * FROM evaluate_match_cases()";

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            var rows = await connection.QueryAsync<MatchBenchmarkResult>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            return rows.ToList();
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Match benchmark failed");
            throw new DatabaseUnavailableException(
                "The name database could not be queried.",
                exception);
        }
    }

    public async Task<NameComparisonResult?> CompareNamesAsync(
        string leftName,
        string rightName,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT * FROM compare_company_names(@LeftName, @RightName)";

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            return await connection.QuerySingleOrDefaultAsync<NameComparisonResult>(
                new CommandDefinition(
                    sql,
                    new { LeftName = leftName, RightName = rightName },
                    cancellationToken: cancellationToken));
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Name comparison failed");
            throw new DatabaseUnavailableException(
                "The name database could not be queried.",
                exception);
        }
    }

    public async Task<IReadOnlyList<TokenPairResult>> ExplainTokensAsync(
        string normalisedLeft,
        string normalisedRight,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT * FROM fn_explain_token_pairs(@LeftName, @RightName)";

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            var rows = await connection.QueryAsync<TokenPairResult>(
                new CommandDefinition(
                    sql,
                    new { LeftName = normalisedLeft, RightName = normalisedRight },
                    cancellationToken: cancellationToken));
            return rows.ToList();
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Token explanation failed");
            throw new DatabaseUnavailableException(
                "The name database could not be queried.",
                exception);
        }
    }

    public async Task<IReadOnlyList<StoredCompany>> ListStoredCompaniesAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT * FROM fn_list_stored_companies()";

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            var rows = await connection.QueryAsync<StoredCompany>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            return rows.ToList();
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Listing stored companies failed");
            throw new DatabaseUnavailableException(
                "The name database could not be queried.",
                exception);
        }
    }

    public async Task<StoredCompany> AddCompanyAsync(
        string companyName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                "CALL usp_add_company(@p_company_name, @p_company_id)",
                connection);
            command.Parameters.AddWithValue("p_company_name", companyName.Trim());
            var idParameter = new NpgsqlParameter("p_company_id", NpgsqlDbType.Integer)
            {
                Direction = ParameterDirection.InputOutput,
                Value = DBNull.Value
            };
            command.Parameters.Add(idParameter);
            await command.ExecuteNonQueryAsync(cancellationToken);

            var companyId = (int)idParameter.Value!;
            return await connection.QuerySingleAsync<StoredCompany>(
                new CommandDefinition(
                    "SELECT * FROM fn_get_stored_company(@CompanyId)",
                    new { CompanyId = companyId },
                    cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (exception.SqlState is "22023" or "P0001")
        {
            throw new CompanySaveException(exception.MessageText);
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Adding a company failed");
            throw new DatabaseUnavailableException(
                "The name database could not be queried.",
                exception);
        }
    }

    public async Task SetCompanyActiveAsync(
        int companyId,
        bool active,
        CancellationToken cancellationToken = default)
    {
        const string sql = "CALL usp_set_company_active(@CompanyId, @Active)";

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { CompanyId = companyId, Active = active },
                    cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (exception.SqlState is "22023" or "P0001")
        {
            throw new CompanySaveException(exception.MessageText);
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Setting company {CompanyId} active to {Active} failed", companyId, active);
            throw new DatabaseUnavailableException(
                "The name database could not be queried.",
                exception);
        }
    }

    private static bool IsDatabaseException(Exception exception) =>
        exception is NpgsqlException or TimeoutException;

    public async Task<IReadOnlyList<CompanyGroupMember>> GetCompanyGroupAsync(
        string companyName,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT * FROM fn_company_group(@CompanyName)";
        return await QueryRelationshipAsync<CompanyGroupMember>(sql, companyName, cancellationToken);
    }

    public async Task<IReadOnlyList<CompanyWorkLink>> GetCompanyLinksAsync(
        string companyName,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT * FROM fn_company_links(@CompanyName)";
        return await QueryRelationshipAsync<CompanyWorkLink>(sql, companyName, cancellationToken);
    }

    public async Task SetParentCompanyAsync(
        string companyName,
        string parentName,
        CancellationToken cancellationToken = default)
    {
        const string sql = "CALL usp_set_parent_company(@CompanyName, @ParentName)";
        await ExecuteRelationshipAsync(
            sql,
            new { CompanyName = companyName.Trim(), ParentName = parentName.Trim() },
            cancellationToken);
    }

    public async Task EnsureStoredCompanyAsync(
        string companyName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            var ids = (await connection.QueryAsync<int>(
                new CommandDefinition(
                    """
                    SELECT company_id
                    FROM company
                    WHERE normalised_name = fn_normalise_company_name(@CompanyName)
                    """,
                    new { CompanyName = companyName.Trim() },
                    cancellationToken: cancellationToken))).ToList();

            if (ids.Count > 1)
            {
                throw new CompanySaveException(
                    $"More than one stored company matches '{companyName.Trim()}'.");
            }

            if (ids.Count == 0)
            {
                await AddCompanyAsync(companyName, cancellationToken);
            }
        }
        catch (CompanySaveException)
        {
            throw;
        }
        catch (PostgresException exception) when (exception.SqlState is "22023" or "P0001")
        {
            throw new CompanySaveException(exception.MessageText);
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Checking a stored company failed");
            throw new DatabaseUnavailableException("The name database could not be queried.", exception);
        }
    }

    public async Task AddCompanyLinkAsync(
        string companyName,
        string partnerName,
        string linkType,
        string? note,
        CancellationToken cancellationToken = default)
    {
        const string sql = "CALL usp_add_company_link(@CompanyName, @PartnerName, @LinkType, @Note)";
        await ExecuteRelationshipAsync(
            sql,
            new
            {
                CompanyName = companyName.Trim(),
                PartnerName = partnerName.Trim(),
                LinkType = linkType.Trim(),
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            },
            cancellationToken);
    }

    private async Task<IReadOnlyList<T>> QueryRelationshipAsync<T>(
        string sql,
        string companyName,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            var rows = await connection.QueryAsync<T>(
                new CommandDefinition(sql, new { CompanyName = companyName.Trim() }, cancellationToken: cancellationToken));
            return rows.ToList();
        }
        catch (PostgresException exception) when (exception.SqlState is "22023" or "P0001")
        {
            throw new CompanySaveException(exception.MessageText);
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Company relationship query failed");
            throw new DatabaseUnavailableException("The name database could not be queried.", exception);
        }
    }

    private async Task ExecuteRelationshipAsync(
        string sql,
        object parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (exception.SqlState is "22023" or "P0001")
        {
            throw new CompanySaveException(exception.MessageText);
        }
        catch (Exception exception) when (IsDatabaseException(exception))
        {
            _logger.LogError(exception, "Company relationship update failed");
            throw new DatabaseUnavailableException("The name database could not be queried.", exception);
        }
    }
}
