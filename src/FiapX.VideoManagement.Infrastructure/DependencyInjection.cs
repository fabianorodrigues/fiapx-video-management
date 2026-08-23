using FiapX.VideoManagement.Application.Abstractions;
using FiapX.VideoManagement.Application.Common;
using FiapX.VideoManagement.Application.Videos;
using FiapX.VideoManagement.Infrastructure.Cache;
using FiapX.VideoManagement.Infrastructure.Identity;
using FiapX.VideoManagement.Infrastructure.Persistence;
using FiapX.VideoManagement.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FiapX.VideoManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddVideoManagementInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var postgresConnectionString = configuration.GetConnectionString("Postgres")
            ?? configuration["POSTGRES_CONNECTION_STRING"]
            ?? "Host=localhost;Port=5432;Database=fiapx_videos;Username=fiapx;Password=fiapx_dev_password";

        services.AddDbContext<VideoDbContext>(options =>
            options.UseNpgsql(postgresConnectionString));

        services.AddScoped<IVideoDataStore, EfVideoDataStore>();
        services.AddScoped<VideoService>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ICurrentUser, DevelopmentCurrentUser>();

        var redisConnectionString = configuration["REDIS_CONNECTION_STRING"] ?? "localhost:6379";
        var cacheTtlSeconds = configuration.GetValue("CACHE_TTL_SECONDS", 30);
        services.AddSingleton<IVideoCache>(_ =>
            new RedisVideoCache(redisConnectionString, TimeSpan.FromSeconds(cacheTtlSeconds)));

        services.AddSingleton(S3StorageOptions.FromConfiguration(configuration));
        services.AddSingleton<IVideoStorage, S3VideoStorage>();

        return services;
    }
}
