using System.Net;
using System.Text;
using System.Text.Json;
using FiapX.VideoManagement.Application.Portas;
using FiapX.VideoManagement.Application.Videos;
using FiapX.VideoManagement.Application.Videos.Processamento;
using FiapX.VideoManagement.Domain.Videos;
using FiapX.VideoManagement.Infrastructure.Mensageria.RabbitMq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;

namespace FiapX.VideoManagement.Tests;

public sealed class ConsumidorStatusRabbitMqTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Dispatcher_resolves_started_handler_and_applies_status_event()
    {
        var video = CreateVideo();
        await using var provider = CreateDispatchProvider(video);
        var dispatcher = new DespachanteEventoStatus();
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new VideoProcessingStarted(Guid.NewGuid(), video.Id, video.UserId, Now.AddMinutes(1)),
            JsonOptions);

        var result = await dispatcher.DispatchAsync(
            provider,
            RabbitMqTopology.StartedRoutingKey,
            payload,
            CancellationToken.None);

        Assert.True(result.IsInfrastructureContractValid);
        Assert.Equal(DesfechoTratamentoEventoProcessamento.Applied, result.HandlingResult?.Outcome);
        Assert.Equal(VideoStatus.Processando, video.Status);
    }

    [Fact]
    public async Task Dispatcher_resolves_completed_handler_and_applies_status_event()
    {
        var video = CreateVideo();
        await using var provider = CreateDispatchProvider(video);
        var dispatcher = new DespachanteEventoStatus();
        var resultObjectKey = ChavesObjetoVideo.Result(video.UserId, video.Id);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new VideoProcessingCompleted(Guid.NewGuid(), video.Id, video.UserId, resultObjectKey, Now.AddMinutes(2)),
            JsonOptions);

        var result = await dispatcher.DispatchAsync(
            provider,
            RabbitMqTopology.CompletedRoutingKey,
            payload,
            CancellationToken.None);

        Assert.True(result.IsInfrastructureContractValid);
        Assert.Equal(DesfechoTratamentoEventoProcessamento.Applied, result.HandlingResult?.Outcome);
        Assert.Equal(VideoStatus.Concluido, video.Status);
        Assert.Equal(resultObjectKey, video.ResultObjectKey);
    }

    [Fact]
    public async Task Dispatcher_resolves_failed_handler_and_sends_notification()
    {
        var video = CreateVideo();
        var notifications = new DispatchNotifications();
        await using var provider = CreateDispatchProvider(video, notifications);
        var dispatcher = new DespachanteEventoStatus();
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new VideoProcessingFailed(Guid.NewGuid(), video.Id, video.UserId, "PROCESSING_FAILED", "safe failure", Now.AddMinutes(3)),
            JsonOptions);

        var result = await dispatcher.DispatchAsync(
            provider,
            RabbitMqTopology.FailedRoutingKey,
            payload,
            CancellationToken.None);

        Assert.True(result.IsInfrastructureContractValid);
        Assert.Equal(DesfechoTratamentoEventoProcessamento.Applied, result.HandlingResult?.Outcome);
        Assert.Equal(VideoStatus.Erro, video.Status);
        Assert.Single(notifications.Sent);
    }

    [Fact]
    public async Task Dispatcher_rejects_unknown_routing_key_as_infrastructure_contract_error()
    {
        var dispatcher = new DespachanteEventoStatus();

        var result = await dispatcher.DispatchAsync(
            serviceProvider: new EmptyServiceProvider(),
            routingKey: "video.processing.unknown",
            body: Encoding.UTF8.GetBytes("{}"),
            CancellationToken.None);

        Assert.False(result.IsInfrastructureContractValid);
        Assert.Null(result.HandlingResult);
    }

    [Theory]
    [InlineData(RabbitMqTopology.StartedRoutingKey)]
    [InlineData(RabbitMqTopology.CompletedRoutingKey)]
    [InlineData(RabbitMqTopology.FailedRoutingKey)]
    public async Task Dispatcher_rejects_null_status_payload_before_handler_resolution(string routingKey)
    {
        var dispatcher = new DespachanteEventoStatus();

        var result = await dispatcher.DispatchAsync(
            serviceProvider: new EmptyServiceProvider(),
            routingKey,
            body: Encoding.UTF8.GetBytes("null"),
            CancellationToken.None);

        Assert.False(result.IsInfrastructureContractValid);
        Assert.Null(result.HandlingResult);
    }

    [Fact]
    public async Task Dispatcher_rejects_malformed_status_payload_before_handler_resolution()
    {
        var dispatcher = new DespachanteEventoStatus();

        var result = await dispatcher.DispatchAsync(
            serviceProvider: new EmptyServiceProvider(),
            routingKey: RabbitMqTopology.CompletedRoutingKey,
            body: Encoding.UTF8.GetBytes("{not-json"),
            CancellationToken.None);

        Assert.False(result.IsInfrastructureContractValid);
        Assert.Null(result.HandlingResult);
    }

    [Theory]
    [MemberData(nameof(AttemptHeaderValues))]
    public void Attempt_header_uses_original_as_first_attempt_and_parses_common_types(object? value, int expected)
    {
        Dictionary<string, object?>? headers = value is null
            ? null
            : new Dictionary<string, object?> { [RabbitMqAttemptHeader.HeaderName] = value };

        Assert.Equal(expected, RabbitMqAttemptHeader.GetCurrentAttempt(headers));
    }

    public static TheoryData<object?, int> AttemptHeaderValues() =>
        new()
        {
            { null, 1 },
            { 2, 2 },
            { 3L, 3 },
            { (short)4, 4 },
            { (byte)5, 5 },
            { (sbyte)6, 6 },
            { 7U, 7 },
            { 8UL, 8 },
            { "9", 9 },
            { Encoding.UTF8.GetBytes("10"), 10 },
            { new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("11")), 11 },
            { BitConverter.GetBytes(IPAddress.HostToNetworkOrder(12)), 12 },
            { BitConverter.GetBytes(IPAddress.HostToNetworkOrder(13L)), 13 },
            { 0, 1 },
            { -1, 1 },
            { "invalid", 1 },
            { new object(), 1 }
        };

    [Fact]
    public void Retry_properties_preserve_status_metadata_and_replace_attempt_and_expiration()
    {
        var source = new BasicProperties
        {
            ContentType = "application/json",
            MessageId = "event-1",
            CorrelationId = "video-1",
            Type = "VideoProcessingCompleted",
            Expiration = "1000",
            Headers = new Dictionary<string, object?>
            {
                ["x-existing"] = "keep",
                ["expiration"] = "old",
                [RabbitMqAttemptHeader.HeaderName] = 1
            }
        };

        var retry = RabbitMqPublishProperties.ForRetry(source, nextAttempt: 2, retryDelayMs: 5000);

        Assert.True(retry.Persistent);
        Assert.Equal("application/json", retry.ContentType);
        Assert.Equal("event-1", retry.MessageId);
        Assert.Equal("video-1", retry.CorrelationId);
        Assert.Equal("VideoProcessingCompleted", retry.Type);
        Assert.Equal("5000", retry.Expiration);
        Assert.Equal("keep", retry.Headers!["x-existing"]);
        Assert.Equal(2, retry.Headers[RabbitMqAttemptHeader.HeaderName]);
        Assert.False(retry.Headers.ContainsKey("expiration"));
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static Video CreateVideo()
    {
        var videoId = Guid.NewGuid();
        return Video.Registrar(
            videoId,
            "user-1",
            "user-1@fiapx.local",
            "video.mp4",
            ChavesObjetoVideo.Original("user-1", videoId),
            ChavesObjetoVideo.Result("user-1", videoId),
            Now);
    }

    private static ServiceProvider CreateDispatchProvider(
        Video video,
        IEnviadorNotificacao? notifications = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRepositorioVideo>(new DispatchStore(video));
        services.AddSingleton<ICacheVideo, DispatchCache>();
        services.AddSingleton(notifications ?? new DispatchNotifications());
        services.AddSingleton<ILogger<ManipuladorProcessamentoVideoIniciado>>(NullLogger<ManipuladorProcessamentoVideoIniciado>.Instance);
        services.AddSingleton<ILogger<ManipuladorProcessamentoVideoConcluido>>(NullLogger<ManipuladorProcessamentoVideoConcluido>.Instance);
        services.AddSingleton<ILogger<ManipuladorProcessamentoVideoFalhou>>(NullLogger<ManipuladorProcessamentoVideoFalhou>.Instance);
        services.AddTransient<ManipuladorProcessamentoVideoIniciado>();
        services.AddTransient<ManipuladorProcessamentoVideoConcluido>();
        services.AddTransient<ManipuladorProcessamentoVideoFalhou>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class DispatchStore : IRepositorioVideo
    {
        private readonly Video _video;

        public DispatchStore(Video video)
        {
            _video = video;
        }

        public Task AdicionarAsync(Video video, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Video>> ListarPorUsuarioAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Video>>(_video.UserId == userId ? [_video] : []);

        public Task<Video?> ObterPorUsuarioAsync(string userId, Guid videoId, CancellationToken cancellationToken) =>
            Task.FromResult(_video.UserId == userId && _video.Id == videoId ? _video : null);

        public Task<Video?> ObterPorIdAsync(Guid videoId, CancellationToken cancellationToken) =>
            Task.FromResult(_video.Id == videoId ? _video : null);

        public Task SalvarAlteracoesAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class DispatchCache : ICacheVideo
    {
        public Task<IReadOnlyList<VideoResponse>?> ObterVideosAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VideoResponse>?>(null);

        public Task<VideoResponse?> ObterVideoAsync(string userId, Guid videoId, CancellationToken cancellationToken) =>
            Task.FromResult<VideoResponse?>(null);

        public Task SalvarVideosAsync(string userId, IReadOnlyList<VideoResponse> videos, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SalvarVideoAsync(string userId, Guid videoId, VideoResponse video, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RemoverVideosAsync(string userId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RemoverVideoAsync(string userId, Guid videoId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class DispatchNotifications : IEnviadorNotificacao
    {
        public List<NotificacaoFalhaProcessamento> Sent { get; } = [];

        public Task EnviarFalhaProcessamentoAsync(NotificacaoFalhaProcessamento notification, CancellationToken cancellationToken)
        {
            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }
}
