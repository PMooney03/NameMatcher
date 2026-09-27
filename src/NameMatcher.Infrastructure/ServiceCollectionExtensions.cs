using Microsoft.Extensions.DependencyInjection;
using NameMatcher.Core;
using Npgsql;

namespace NameMatcher.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNameMatcherDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        services.AddSingleton(dataSource);
        services.AddScoped<ICompanyRepository, CompanyRepository>();
        return services;
    }
}
