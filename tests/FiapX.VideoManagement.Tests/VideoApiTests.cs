using System.Net;
using System.Net.Http.Json;
using FiapX.VideoManagement.Application.Abstractions;
using FiapX.VideoManagement.Application.Videos;
using FiapX.VideoManagement.Domain.Videos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace FiapX.VideoManagement.Tests;

public sealed class VideoApiTests
{
    [Fact]
    public async Task Post_get_and_download_conflict_follow_http_contracts()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureLogging(logging => logging.ClearProviders());
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IVideoDataStore>();
                    services.RemoveAll<IVideoCache>();
                    services.RemoveAll<IVideoStorage>();
                    services.RemoveAll<ICurrentUser>();
                    services.RemoveAll<IClock>();

                    services.AddSingleton<IVideoDataStore, ApiInMemoryVideoDataStore>();
                    services.AddSingleton<IVideoCache, ApiInMemoryVideoCache>();
                    services.AddSingleton<IVideoStorage, ApiStubVideoStorage>();
                    services.AddSingleton<ICurrentUser>(new ApiStubCurrentUser("api-user", "api-user@fiapx.local"));
                    services.AddSingleton<IClock>(new ApiFixedClock(new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero)));
                });
            });

        using var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/videos", new CreateVideoRequest("api.mp4", "video/mp4"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<CreateVideoResponse>();
        Assert.NotNull(created);
        Assert.Equal("RECEBIDO", created.Status);
        Assert.Equal("http://localhost:9000/upload", created.UploadUrl);

        var detail = await client.GetFromJsonAsync<VideoResponse>($"/videos/{created.VideoId}");
        Assert.NotNull(detail);
        Assert.Equal(created.VideoId, detail.VideoId);
        Assert.Equal("api-user", detail.UserId);

        var downloadResponse = await client.GetAsync($"/videos/{created.VideoId}/download");
        Assert.Equal(HttpStatusCode.Conflict, downloadResponse.StatusCode);
    }

    private sealed class ApiInMemoryVideoDataStore : IVideoDataStore
    {
        private readonly List<Video> _videos = [];

        public Task AddAsync(Video video, CancellationToken cancellationToken)
        {
            _videos.Add(video);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Video>> ListByUserAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Video>>(_videos.Where(video => video.UserId == userId).ToArray());

        public Task<Video?> GetByUserAsync(string userId, Guid videoId, CancellationToken cancellationToken) =>
            Task.FromResult(_videos.FirstOrDefault(video => video.Id == videoId && video.UserId == userId));
    }

    private sealed class ApiInMemoryVideoCache : IVideoCache
    {
        public Task<IReadOnlyList<VideoResponse>?> GetVideosAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VideoResponse>?>(null);

        public Task<VideoResponse?> GetVideoAsync(string userId, Guid videoId, CancellationToken cancellationToken) =>
            Task.FromResult<VideoResponse?>(null);

        public Task SetVideosAsync(string userId, IReadOnlyList<VideoResponse> videos, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SetVideoAsync(string userId, Guid videoId, VideoResponse video, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RemoveVideosAsync(string userId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ApiStubVideoStorage : IVideoStorage
    {
        public Task<PresignedUrl> CreateUploadUrlAsync(string objectKey, string contentType, CancellationToken cancellationToken) =>
            Task.FromResult(new PresignedUrl("http://localhost:9000/upload", 900));

        public Task<PresignedUrl> CreateDownloadUrlAsync(string objectKey, CancellationToken cancellationToken) =>
            Task.FromResult(new PresignedUrl("http://localhost:9000/download", 900));
    }

    private sealed record ApiStubCurrentUser(string UserId, string Email) : ICurrentUser;

    private sealed record ApiFixedClock(DateTimeOffset UtcNow) : IClock;
}
