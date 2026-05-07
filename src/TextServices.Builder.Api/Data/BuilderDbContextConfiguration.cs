using Microsoft.EntityFrameworkCore;

namespace TextServices.Builder.Api.Data;

public static class BuilderDbContextConfiguration
{
    private const string RunMigrationsKey = "RunMigrations";

    public static void TryRunMigrations(IConfiguration configuration, ILogger logger)
    {
        if (!configuration.GetValue(RunMigrationsKey, false)) return;

        var connectionString = configuration.GetConnectionString("BuilderDb")
            ?? throw new InvalidOperationException("ConnectionStrings:BuilderDb is required.");

        var options = new DbContextOptionsBuilder<BuilderDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        using var context = new BuilderDbContext(options);
        var pending = context.Database.GetPendingMigrations().ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("No pending migrations to run");
            return;
        }

        logger.LogInformation("Running migrations: {Migrations}", string.Join(", ", pending));
        context.Database.Migrate();
    }
}
