using System.Collections.Concurrent;
using System.Net.Http.Json;
using FiapX.VideoManagement.Application.Abstractions;
using FiapX.VideoManagement.Application.Common;
using FiapX.VideoManagement.Application.ProcessingEvents;
using FiapX.VideoManagement.Application.Videos;
using FiapX.VideoManagement.Domain.Videos;
using FiapX.VideoManagement.Infrastructure.Mail;
using FiapX.VideoManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FiapX.VideoManagement.Tests;

public sealed class ProcessingEventPostgresIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handlers_persist_started_completed_and_failed_with_real_postgresql()
    {
        if (!PostgresIntegrationSettings.Enabled)
        {
            return;
        }

        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var provider = CreateProvider(database.ConnectionString);
        await MigrateAsync(provider);

        var startedVideo = await SeedVideoAsync(provider, "started-user");
        var completedVideo = await SeedVideoAsync(provider, "completed-user");
        var failedVideo = await SeedVideoAsync(provider, "failed-user");

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<VideoProcessingStartedHandler>().HandleAsync(
                new VideoProcessingStarted(
                    Guid.NewGuid(),
                    startedVideo.Id,
                    startedVideo.UserId,
                    DateTimeOffset.Parse("2026-08-23T13:30:00-03:00")),
                CancellationToken.None);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<VideoProcessingCompletedHandler>().HandleAsync(
                new VideoProcessingCompleted(
                    Guid.NewGuid(),
                    completedVideo.Id,
                    completedVideo.UserId,
                    VideoObjectKeys.Result(completedVideo.UserId, completedVideo.Id),
                    Now.AddMinutes(2)),
                CancellationToken.None);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<VideoProcessingFailedHandler>().HandleAsync(
                new VideoProcessingFailed(
                    Guid.NewGuid(),
                    failedVideo.Id,
                    failedVideo.UserId,
                    "PROCESSING_FAILED",
                    "safe failure",
                    Now.AddMinutes(3)),
                CancellationToken.None);
        }

        var started = await LoadVideoAsync(provider, startedVideo.Id);
        var completed = await LoadVideoAsync(provider, completedVideo.Id);
        var failed = await LoadVideoAsync(provider, failedVideo.Id);

        Assert.Equal(VideoStatus.Processando, started.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-08-23T16:30:00Z"), started.ProcessingStartedAt);
        Assert.Equal(VideoStatus.Concluido, completed.Status);
        Assert.Equal(VideoObjectKeys.Result(completed.UserId, completed.Id), completed.ResultObjectKey);
        Assert.Equal(VideoStatus.Erro, failed.Status);
        Assert.Equal("PROCESSING_FAILED", failed.ErrorCode);
        Assert.Equal("safe failure", failed.ErrorMessage);
    }

    [Fact]
    public async Task Failed_concurrent_duplicates_use_real_postgresql_optimistic_concurrency()
    {
        if (!PostgresIntegrationSettings.Enabled)
        {
            return;
        }

        await using var database = await PostgresTestDatabase.CreateAsync();
        var notificationSender = new IntegrationNotificationSender();
        var saveGate = new ConcurrencySaveGate();
        await using var provider = CreateProvider(database.ConnectionString, notificationSender, saveGate);
        await MigrateAsync(provider);
        var video = await SeedVideoAsync(provider, "failed-concurrent");

        var failed = new VideoProcessingFailed(
            Guid.NewGuid(),
            video.Id,
            video.UserId,
            "PROCESSING_FAILED",
            "safe",
            Now.AddMinutes(3));

        await Task.WhenAll(
            HandleFailedAsync(provider, failed, priority: 0),
            HandleFailedAsync(provider, failed with { EventId = Guid.NewGuid() }, priority: 0));

        var persisted = await LoadVideoAsync(provider, video.Id);
        Assert.Equal(VideoStatus.Erro, persisted.Status);
        Assert.Equal(1, notificationSender.AttemptCount);
    }

    [Fact]
    public async Task Started_and_completed_concurrently_finish_completed_with_separate_scopes()
    {
        if (!PostgresIntegrationSettings.Enabled)
        {
            return;
        }

        await using var database = await PostgresTestDatabase.CreateAsync();
        var saveGate = new ConcurrencySaveGate();
        await using var provider = CreateProvider(database.ConnectionString, saveGate: saveGate);
        await MigrateAsync(provider);
        var video = await SeedVideoAsync(provider, "started-completed");

        await Task.WhenAll(
            HandleStartedAsync(
                provider,
                new VideoProcessingStarted(Guid.NewGuid(), video.Id, video.UserId, Now.AddMinutes(1)),
                priority: 0),
            HandleCompletedAsync(
                provider,
                new VideoProcessingCompleted(
                    Guid.NewGuid(),
                    video.Id,
                    video.UserId,
                    VideoObjectKeys.Result(video.UserId, video.Id),
                    Now.AddMinutes(2)),
                priority: 1));

        var persisted = await LoadVideoAsync(provider, video.Id);
        Assert.Equal(VideoStatus.Concluido, persisted.Status);
        Assert.Equal(Now.AddMinutes(1), persisted.ProcessingStartedAt);
        Assert.Equal(Now.AddMinutes(2), persisted.ProcessingFinishedAt);
    }

    [Fact]
    public async Task Started_and_failed_concurrently_do_not_regress_terminal_error_status()
    {
        if (!PostgresIntegrationSettings.Enabled)
        {
            return;
        }

        await using var database = await PostgresTestDatabase.CreateAsync();
        var notificationSender = new IntegrationNotificationSender();
        var saveGate = new ConcurrencySaveGate();
        await using var provider = CreateProvider(database.ConnectionString, notificationSender, saveGate);
        await MigrateAsync(provider);
        var video = await SeedVideoAsync(provider, "started-failed");

        await Task.WhenAll(
            HandleFailedAsync(
                provider,
                new VideoProcessingFailed(Guid.NewGuid(), video.Id, video.UserId, "PROCESSING_FAILED", "safe", Now.AddMinutes(2)),
                priority: 0),
            HandleStartedAsync(
                provider,
                new VideoProcessingStarted(Guid.NewGuid(), video.Id, video.UserId, Now.AddMinutes(1)),
                priority: 1));

        var persisted = await LoadVideoAsync(provider, video.Id);
        Assert.Equal(VideoStatus.Erro, persisted.Status);
        Assert.Equal(1, notificationSender.AttemptCount);
    }

    [Fact]
    public async Task Failed_handler_keeps_error_when_smtp_is_unavailable()
    {
        if (!PostgresIntegrationSettings.Enabled)
        {
            return;
        }

        await using var database = await PostgresTestDatabase.CreateAsync();
        var unavailableSmtp = new SmtpNotificationSender(
            new SmtpNotificationOptions
            {
                Host = "localhost",
                Port = 1,
                From = "no-reply@fiapx.local",
                Timeout = TimeSpan.FromMilliseconds(250)
            });
        await using var provider = CreateProvider(database.ConnectionString, unavailableSmtp);
        await MigrateAsync(provider);
        var video = await SeedVideoAsync(provider, "smtp-unavailable");

        await HandleFailedAsync(
            provider,
            new VideoProcessingFailed(Guid.NewGuid(), video.Id, video.UserId, "PROCESSING_FAILED", "safe", Now),
            priority: 0);

        var persisted = await LoadVideoAsync(provider, video.Id);
        Assert.Equal(VideoStatus.Erro, persisted.Status);
    }

    [Fact]
    public async Task Failed_handler_sends_email_to_real_mailpit()
    {
        if (!PostgresIntegrationSettings.Enabled || !MailpitIntegrationSettings.Enabled)
        {
            return;
        }

        using var http = new HttpClient { BaseAddress = new Uri(MailpitIntegrationSettings.WebUrl) };
        await TryClearMailpitAsync(http);

        await using var database = await PostgresTestDatabase.CreateAsync();
        var smtp = new SmtpNotificationSender(
            new SmtpNotificationOptions
            {
                Host = MailpitIntegrationSettings.SmtpHost,
                Port = MailpitIntegrationSettings.SmtpPort,
                From = "no-reply@fiapx.local",
                Timeout = TimeSpan.FromSeconds(5)
            });
        await using var provider = CreateProvider(database.ConnectionString, smtp);
        await MigrateAsync(provider);
        var video = await SeedVideoAsync(provider, "mailpit-user");

        await HandleFailedAsync(
            provider,
            new VideoProcessingFailed(Guid.NewGuid(), video.Id, video.UserId, "PROCESSING_FAILED", "unsafe internal detail", Now),
            priority: 0);

        var mailpitJson = await http.GetStringAsync("/api/v1/messages");
        Assert.Contains("FIAP X video processing failed", mailpitJson, StringComparison.Ordinal);
        Assert.Contains(video.Id.ToString(), mailpitJson, StringComparison.Ordinal);
    }

    private static ServiceProvider CreateProvider(
        string connectionString,
        INotificationSender? notificationSender = null,
        ConcurrencySaveGate? saveGate = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddDbContext<VideoDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ScopedOperationContext>();
        services.AddSingleton(saveGate ?? new ConcurrencySaveGate(disabled: true));
        services.AddScoped<IVideoDataStore, GatedEfVideoDataStore>();
        services.AddSingleton<IVideoCache, IntegrationCache>();
        services.AddSingleton(notificationSender ?? new IntegrationNotificationSender());
        services.AddScoped<VideoProcessingStartedHandler>();
        services.AddScoped<VideoProcessingCompletedHandler>();
        services.AddScoped<VideoProcessingFailedHandler>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task MigrateAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VideoDbContext>();
        await db.Database.MigrateAsync();
    }

    private static async Task<Video> SeedVideoAsync(ServiceProvider provider, string userId)
    {
        await using var scope = provider.CreateAsyncScope();
        var videos = scope.ServiceProvider.GetRequiredService<IVideoDataStore>();
        var videoId = Guid.NewGuid();
        var video = Video.Register(
            videoId,
            userId,
            $"{userId}@fiapx.local",
            "video.mp4",
            VideoObjectKeys.Original(userId, videoId),
            VideoObjectKeys.Result(userId, videoId),
            Now);

        await videos.AddAsync(video, CancellationToken.None);
        return video;
    }

    private static async Task<Video> LoadVideoAsync(ServiceProvider provider, Guid videoId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VideoDbContext>();
        return await db.Videos.AsNoTracking().SingleAsync(video => video.Id == videoId);
    }

    private static async Task HandleStartedAsync(
        ServiceProvider provider,
        VideoProcessingStarted @event,
        int priority)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopedOperationContext>().SavePriority = priority;
        await scope.ServiceProvider.GetRequiredService<VideoProcessingStartedHandler>().HandleAsync(@event, CancellationToken.None);
    }

    private static async Task HandleCompletedAsync(
        ServiceProvider provider,
        VideoProcessingCompleted @event,
        int priority)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopedOperationContext>().SavePriority = priority;
        await scope.ServiceProvider.GetRequiredService<VideoProcessingCompletedHandler>().HandleAsync(@event, CancellationToken.None);
    }

    private static async Task HandleFailedAsync(
        ServiceProvider provider,
        VideoProcessingFailed @event,
        int priority)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopedOperationContext>().SavePriority = priority;
        await scope.ServiceProvider.GetRequiredService<VideoProcessingFailedHandler>().HandleAsync(@event, CancellationToken.None);
    }

    private static async Task TryClearMailpitAsync(HttpClient http)
    {
        try
        {
            await http.DeleteAsync("/api/v1/messages");
        }
        catch
        {
        }
    }

    private sealed class GatedEfVideoDataStore : IVideoDataStore
    {
        private readonly VideoDbContext _dbContext;
        private readonly ConcurrencySaveGate _saveGate;
        private readonly ScopedOperationContext _operationContext;
        private bool _saveGateUsed;

        public GatedEfVideoDataStore(
            VideoDbContext dbContext,
            ConcurrencySaveGate saveGate,
            ScopedOperationContext operationContext)
        {
            _dbContext = dbContext;
            _saveGate = saveGate;
            _operationContext = operationContext;
        }

        public async Task AddAsync(Video video, CancellationToken cancellationToken)
        {
            await _dbContext.Videos.AddAsync(video, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Video>> ListByUserAsync(string userId, CancellationToken cancellationToken) =>
            await _dbContext.Videos
                .AsNoTracking()
                .Where(video => video.UserId == userId)
                .ToListAsync(cancellationToken);

        public Task<Video?> GetByUserAsync(string userId, Guid videoId, CancellationToken cancellationToken) =>
            _dbContext.Videos
                .AsNoTracking()
                .FirstOrDefaultAsync(video => video.Id == videoId && video.UserId == userId, cancellationToken);

        public Task<Video?> GetByIdAsync(Guid videoId, CancellationToken cancellationToken) =>
            _dbContext.Videos.FirstOrDefaultAsync(video => video.Id == videoId, cancellationToken);

        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (!_saveGateUsed)
            {
                _saveGateUsed = true;
                await _saveGate.WaitBeforeFirstSaveAsync(_operationContext.SavePriority, cancellationToken);
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _dbContext.ChangeTracker.Clear();
                throw new VideoUpdateConcurrencyException("Video was changed concurrently.", ex);
            }
            finally
            {
                _saveGate.SignalAfterSave(_operationContext.SavePriority);
            }
        }
    }

    private sealed class ScopedOperationContext
    {
        public int SavePriority { get; set; }
    }

    private sealed class ConcurrencySaveGate
    {
        private readonly bool _disabled;
        private readonly TaskCompletionSource _bothWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _priorityZeroSaved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _waiting;

        public ConcurrencySaveGate(bool disabled = false)
        {
            _disabled = disabled;
        }

        public async Task WaitBeforeFirstSaveAsync(int priority, CancellationToken cancellationToken)
        {
            if (_disabled)
            {
                return;
            }

            if (Interlocked.Increment(ref _waiting) == 2)
            {
                _bothWaiting.TrySetResult();
            }

            await _bothWaiting.Task.WaitAsync(cancellationToken);

            if (priority > 0)
            {
                await _priorityZeroSaved.Task.WaitAsync(cancellationToken);
            }
        }

        public void SignalAfterSave(int priority)
        {
            if (_disabled)
            {
                return;
            }

            if (priority == 0)
            {
                _priorityZeroSaved.TrySetResult();
            }
        }
    }

    private sealed class IntegrationCache : IVideoCache
    {
        public ConcurrentBag<string> RemovedKeys { get; } = [];

        public Task<IReadOnlyList<VideoResponse>?> GetVideosAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VideoResponse>?>(null);

        public Task<VideoResponse?> GetVideoAsync(string userId, Guid videoId, CancellationToken cancellationToken) =>
            Task.FromResult<VideoResponse?>(null);

        public Task SetVideosAsync(string userId, IReadOnlyList<VideoResponse> videos, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SetVideoAsync(string userId, Guid videoId, VideoResponse video, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RemoveVideosAsync(string userId, CancellationToken cancellationToken)
        {
            RemovedKeys.Add(VideoCacheKeys.Videos(userId));
            return Task.CompletedTask;
        }

        public Task RemoveVideoAsync(string userId, Guid videoId, CancellationToken cancellationToken)
        {
            RemovedKeys.Add(VideoCacheKeys.Video(userId, videoId));
            return Task.CompletedTask;
        }
    }

    private sealed class IntegrationNotificationSender : INotificationSender
    {
        private int _attemptCount;

        public int AttemptCount => _attemptCount;

        public Task SendProcessingFailedAsync(ProcessingFailedNotification notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attemptCount);
            return Task.CompletedTask;
        }
    }

    private sealed class PostgresTestDatabase : IAsyncDisposable
    {
        private readonly string _adminConnectionString;

        private PostgresTestDatabase(string databaseName, string connectionString, string adminConnectionString)
        {
            DatabaseName = databaseName;
            ConnectionString = connectionString;
            _adminConnectionString = adminConnectionString;
        }

        public string DatabaseName { get; }
        public string ConnectionString { get; }

        public static async Task<PostgresTestDatabase> CreateAsync()
        {
            var baseBuilder = new NpgsqlConnectionStringBuilder(PostgresIntegrationSettings.ConnectionString);
            var databaseName = $"fiapx_etapa4_{Guid.NewGuid():N}";
            var adminBuilder = new NpgsqlConnectionStringBuilder(baseBuilder.ConnectionString)
            {
                Database = "postgres"
            };
            var testBuilder = new NpgsqlConnectionStringBuilder(baseBuilder.ConnectionString)
            {
                Database = databaseName
            };

            await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE {databaseName}";
            await command.ExecuteNonQueryAsync();

            return new PostgresTestDatabase(databaseName, testBuilder.ConnectionString, adminBuilder.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();

            await using (var terminate = connection.CreateCommand())
            {
                terminate.CommandText = "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @databaseName";
                terminate.Parameters.AddWithValue("databaseName", DatabaseName);
                await terminate.ExecuteNonQueryAsync();
            }

            await using (var drop = connection.CreateCommand())
            {
                drop.CommandText = $"DROP DATABASE IF EXISTS {DatabaseName}";
                await drop.ExecuteNonQueryAsync();
            }
        }
    }

    private static class PostgresIntegrationSettings
    {
        public static bool Enabled =>
            string.Equals(
                Environment.GetEnvironmentVariable("FIAPX_RUN_POSTGRES_INTEGRATION"),
                "true",
                StringComparison.OrdinalIgnoreCase);

        public static string ConnectionString =>
            Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=fiapx_videos;Username=fiapx;Password=fiapx_dev_password";
    }

    private static class MailpitIntegrationSettings
    {
        public static bool Enabled =>
            string.Equals(
                Environment.GetEnvironmentVariable("FIAPX_RUN_MAILPIT_INTEGRATION"),
                "true",
                StringComparison.OrdinalIgnoreCase);

        public static string SmtpHost => Environment.GetEnvironmentVariable("SMTP_HOST") ?? "localhost";

        public static int SmtpPort =>
            int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var port) ? port : 1025;

        public static string WebUrl =>
            Environment.GetEnvironmentVariable("MAILPIT_WEB_URL") ?? "http://localhost:8025";
    }
}
