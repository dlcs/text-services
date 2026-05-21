using Hangfire;
using Hangfire.PostgreSql;

namespace TextServices.Builder.Api.Configuration;

public static class HangfireServiceCollectionExtensions
{
    public static IServiceCollection AddHangfireServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BuilderDb")
            ?? throw new InvalidOperationException("ConnectionStrings:BuilderDb is required.");

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connectionString)));

        services.AddHangfireServer();

        return services;
    }
}
