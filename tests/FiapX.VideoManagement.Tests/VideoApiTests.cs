using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FiapX.VideoManagement.Application.Abstractions;
using FiapX.VideoManagement.Application.Videos;
using FiapX.VideoManagement.Domain.Videos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace FiapX.VideoManagement.Tests;

public sealed class VideoApiTests
{
    private const string TestIssuer = "http://localhost:8081/realms/fiapx";
    private const string TestAudience = "video-management-service";
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("fiapx-video-management-test-signing-key-32"))
    {
        KeyId = "fiapx-test-key"
    };
    private static readonly SymmetricSecurityKey OtherSigningKey = new(Encoding.UTF8.GetBytes("fiapx-video-management-wrong-signing-key"))
    {
        KeyId = "fiapx-wrong-key"
    };

    [Fact]
    public async Task Health_is_anonymous()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_videos_without_jwt_returns_unauthorized()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/videos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_videos_with_valid_jwt_is_authorized()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, CreateToken("alice-sub", "alice@fiapx.local"));

        var response = await client.GetAsync("/videos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_signature_returns_unauthorized()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, CreateToken("alice-sub", "alice@fiapx.local", signingKey: OtherSigningKey));

        var response = await client.GetAsync("/videos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_issuer_returns_unauthorized()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, CreateToken("alice-sub", "alice@fiapx.local", issuer: "http://localhost:8081/realms/other"));

        var response = await client.GetAsync("/videos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_audience_returns_unauthorized()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, CreateToken("alice-sub", "alice@fiapx.local", audience: "other-api"));

        var response = await client.GetAsync("/videos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Expired_token_returns_unauthorized()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, CreateToken("alice-sub", "alice@fiapx.local", expiresAt: DateTime.UtcNow.AddMinutes(-10)));

        var response = await client.GetAsync("/videos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_without_sub_returns_unauthorized()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, CreateToken("alice-sub", "alice@fiapx.local", includeSub: false));

        var response = await client.GetAsync("/videos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_without_email_returns_unauthorized()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, CreateToken("alice-sub", "alice@fiapx.local", includeEmail: false));

        var response = await client.GetAsync("/videos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_get_and_download_conflict_follow_http_contracts()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, CreateToken("api-user-sub", "api-user@fiapx.local"));

        var createResponse = await client.PostAsJsonAsync("/videos", new CreateVideoRequest("api.mp4", "video/mp4"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<CreateVideoResponse>();
        Assert.NotNull(created);
        Assert.Equal("RECEBIDO", created.Status);
        Assert.Equal("http://localhost:9000/upload", created.UploadUrl);

        var detail = await client.GetFromJsonAsync<VideoResponse>($"/videos/{created.VideoId}");
        Assert.NotNull(detail);
        Assert.Equal(created.VideoId, detail.VideoId);
        Assert.Equal("api-user-sub", detail.UserId);

        var downloadResponse = await client.GetAsync($"/videos/{created.VideoId}/download");
        Assert.Equal(HttpStatusCode.Conflict, downloadResponse.StatusCode);
    }

    [Fact]
    public async Task User_id_is_obtained_exclusively_from_sub()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, CreateToken("keycloak-sub-123", "alice@fiapx.local"));

        var createResponse = await client.PostAsJsonAsync("/videos", new CreateVideoRequest("alice.mp4", "video/mp4"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateVideoResponse>();

        Assert.NotNull(created);
        var detail = await client.GetFromJsonAsync<VideoResponse>($"/videos/{created.VideoId}");

        Assert.NotNull(detail);
        Assert.Equal("keycloak-sub-123", detail.UserId);
        Assert.Contains($"/keycloak-sub-123/{created.VideoId}/", detail.OriginalObjectKey);
    }

    [Fact]
    public async Task User_b_cannot_access_user_a_video()
    {
        await using var factory = CreateFactory();
        using var alice = factory.CreateClient();
        using var bob = factory.CreateClient();
        Authorize(alice, CreateToken("alice-sub", "alice@fiapx.local"));
        Authorize(bob, CreateToken("bob-sub", "bob@fiapx.local"));

        var createResponse = await alice.PostAsJsonAsync("/videos", new CreateVideoRequest("alice.mp4", "video/mp4"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateVideoResponse>();

        Assert.NotNull(created);
        var bobResponse = await bob.GetAsync($"/videos/{created.VideoId}");

        Assert.Equal(HttpStatusCode.NotFound, bobResponse.StatusCode);
    }

    [Fact]
    public async Task Get_videos_returns_only_authenticated_user_videos()
    {
        var store = new ApiInMemoryVideoDataStore();
        store.Seed(CreateVideo("alice-sub", "alice.mp4"));
        store.Seed(CreateVideo("bob-sub", "bob.mp4"));
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        Authorize(client, CreateToken("alice-sub", "alice@fiapx.local"));

        var response = await client.GetFromJsonAsync<VideoResponse[]>("/videos");

        Assert.NotNull(response);
        var video = Assert.Single(response);
        Assert.Equal("alice-sub", video.UserId);
        Assert.Equal("alice.mp4", video.OriginalFileName);
    }

    private static WebApplicationFactory<Program> CreateFactory(ApiInMemoryVideoDataStore? store = null)
    {
        store ??= new ApiInMemoryVideoDataStore();

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureLogging(logging => logging.ClearProviders());
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["JWT_METADATA_ADDRESS"] = "http://localhost/.well-known/openid-configuration",
                        ["JWT_ISSUER"] = TestIssuer,
                        ["JWT_AUDIENCE"] = TestAudience
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IVideoDataStore>();
                    services.RemoveAll<IVideoCache>();
                    services.RemoveAll<IVideoStorage>();
                    services.RemoveAll<IClock>();

                    services.AddSingleton<IVideoDataStore>(store);
                    services.AddSingleton<IVideoCache, ApiInMemoryVideoCache>();
                    services.AddSingleton<IVideoStorage, ApiStubVideoStorage>();
                    services.AddSingleton<IClock>(new ApiFixedClock(Now));
                    services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, TestJwtBearerPostConfigureOptions>();
                });
            });
    }

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string CreateToken(
        string subject,
        string email,
        string issuer = TestIssuer,
        string audience = TestAudience,
        DateTime? expiresAt = null,
        SecurityKey? signingKey = null,
        bool includeSub = true,
        bool includeEmail = true)
    {
        var claims = new List<Claim>();

        if (includeSub)
        {
            claims.Add(new Claim("sub", subject));
        }

        if (includeEmail)
        {
            claims.Add(new Claim("email", email));
        }

        var expires = expiresAt ?? DateTime.UtcNow.AddMinutes(30);
        var notBefore = expires <= DateTime.UtcNow
            ? expires.AddMinutes(-5)
            : DateTime.UtcNow.AddMinutes(-5);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: notBefore,
            expires: expires,
            signingCredentials: new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static Video CreateVideo(string userId, string fileName)
    {
        var videoId = Guid.NewGuid();
        return Video.Register(
            videoId,
            userId,
            $"{userId}@fiapx.local",
            fileName,
            VideoObjectKeys.Original(userId, videoId),
            VideoObjectKeys.Result(userId, videoId),
            Now);
    }

    private sealed class TestJwtBearerPostConfigureOptions : IPostConfigureOptions<JwtBearerOptions>
    {
        public void PostConfigure(string? name, JwtBearerOptions options)
        {
            if (!string.Equals(name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
            {
                return;
            }

            var configuration = new OpenIdConnectConfiguration
            {
                Issuer = TestIssuer
            };
            configuration.SigningKeys.Add(SigningKey);

            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            options.TokenValidationParameters.ValidIssuer = TestIssuer;
            options.TokenValidationParameters.ValidAudience = TestAudience;
            options.TokenValidationParameters.IssuerSigningKey = SigningKey;
            options.RequireHttpsMetadata = false;
        }
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

        public void Seed(Video video) => _videos.Add(video);
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

    private sealed record ApiFixedClock(DateTimeOffset UtcNow) : IClock;
}
