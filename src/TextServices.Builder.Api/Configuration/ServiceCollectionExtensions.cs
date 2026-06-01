using System.Net.Http.Headers;
using Amazon.Extensions.NETCore.Setup;
using Amazon.S3;
using Amazon.SimpleNotificationService;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Extensions.Logging;
using TextServices.Builder.Api.Services;
using TextServices.Builder.Api.Services.Notifications;
using TextServices.Infrastructure.Http;
using TextServices.Storage;

namespace TextServices.Builder.Api.Configuration;

public static class ServiceCollectionExtensions
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

    public static IServiceCollection AddAwsServices(this IServiceCollection services, IConfiguration configuration)
    {
        var logger = new SerilogLoggerFactory(Log.Logger).CreateLogger(nameof(ServiceCollectionExtensions));

        services.AddDefaultAWSOptions(configuration.GetAWSOptions());
        services.Configure<S3TextStoreOptions>(configuration.GetSection("TextServices:Storage:S3"));

        // Only register when configured — unconditional registration triggers the AWS credential
        // chain at startup, which hangs in test environments without real credentials.
        if (!string.IsNullOrEmpty(configuration["TextServices:Storage:S3:BucketName"]))
        {
            logger.LogDebug("Using S3 storage for text artefacts");
            services.AddAWSService<IAmazonS3>();
        }

        if (!string.IsNullOrEmpty(configuration["TextServices:Notifications:TopicArn"]))
        {
            logger.LogDebug("Configuring SNS notifications for job status changes");
            services.AddAWSService<IAmazonSimpleNotificationService>();
        }

        return services;
    }

    public static IServiceCollection AddFetchingServices(this IServiceCollection services)
    {
        services.AddHttpClient("Resource", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgents.Builder);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/ld+json", 0.9));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<IResourceFetcher>(sp => new ResourceFetcher(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetService<IAmazonS3>()));

        services.AddSingleton<IManifestReducer, ManifestReducer>()
            .AddSingleton<IManifestSynthesiser, ManifestSynthesiser>()
            .AddScoped<IManifestFetcher, ManifestFetcher>()
            .AddScoped<IAltoFetcher, AltoFetcher>()
            .AddScoped<IVttFetcher, VttFetcher>()
            .AddScoped<IAnnotationPageFetcher, AnnotationPageFetcher>();

        return services;
    }

    public static IServiceCollection AddTextStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ITextStore>(sp =>
        {
            if (!string.IsNullOrEmpty(configuration["TextServices:Storage:S3:BucketName"]))
                return ActivatorUtilities.CreateInstance<S3TextStore>(sp);
            var storage = sp.GetRequiredService<IOptions<TextServicesOptions>>().Value.Storage;
            return ActivatorUtilities.CreateInstance<FileSystemTextStore>(
                sp,
                new FileSystemTextStoreOptions { RootPath = storage.FileSystem.RootPath });
        });

        return services;
    }

    public static IServiceCollection AddNotificationServices(this IServiceCollection services)
    {
        services.AddSingleton<IJobNotifier>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<TextServicesOptions>>();
            if (string.IsNullOrEmpty(opts.Value.Notifications.TopicArn)) return new NullJobNotifier();
            return ActivatorUtilities.CreateInstance<SnsJobNotifier>(sp);
        });

        return services;
    }
}
