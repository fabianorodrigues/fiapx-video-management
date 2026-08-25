using FiapX.VideoManagement.Application.Portas;
using FiapX.VideoManagement.Application.Comum;
using FiapX.VideoManagement.Application.Videos.Processamento;
using FiapX.VideoManagement.Application.Videos;
using FiapX.VideoManagement.Infrastructure.Cache;
using FiapX.VideoManagement.Infrastructure.Notificacoes;
using FiapX.VideoManagement.Infrastructure.Persistencia;
using FiapX.VideoManagement.Infrastructure.Mensageria.RabbitMq;
using FiapX.VideoManagement.Infrastructure.Armazenamento;
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
            ?? throw new InvalidOperationException("POSTGRES_CONNECTION_STRING é obrigatória.");

        services.AddDbContext<VideoDbContext>(options =>
            options.UseNpgsql(postgresConnectionString));

        services.AddScoped<IRepositorioVideo, RepositorioVideoEf>();
        services.AddScoped<ServicoVideo>();
        services.AddScoped<ManipuladorProcessamentoVideoIniciado>();
        services.AddScoped<ManipuladorProcessamentoVideoConcluido>();
        services.AddScoped<ManipuladorProcessamentoVideoFalhou>();
        services.AddSingleton<IRelogio, RelogioSistema>();

        var redisConnectionString = configuration["REDIS_CONNECTION_STRING"] ?? "localhost:6379";
        var cacheTtlSeconds = configuration.GetValue("CACHE_TTL_SECONDS", 30);
        services.AddSingleton<ICacheVideo>(_ =>
            new CacheVideoRedis(redisConnectionString, TimeSpan.FromSeconds(cacheTtlSeconds)));

        services.AddSingleton(OpcoesArmazenamentoS3.FromConfiguration(configuration));
        services.AddSingleton<IArmazenamentoVideo, ArmazenamentoVideoS3>();

        services.AddSingleton(OpcoesNotificacaoSmtp.FromConfiguration(configuration));
        services.AddSingleton<IEnviadorNotificacao, EnviadorNotificacaoSmtp>();

        services.AddSingleton(RabbitMqOptions.FromConfiguration(configuration));
        services.AddSingleton(StatusConsumerOptions.FromConfiguration(configuration));
        services.AddSingleton<DespachanteEventoStatus>();
        services.AddHostedService<ConsumidorStatusRabbitMq>();

        return services;
    }
}
