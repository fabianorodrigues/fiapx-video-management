using System.Text.Json;
using FiapX.VideoManagement.Application.Videos.Processamento;

namespace FiapX.VideoManagement.Tests;

public sealed class ProcessingEventContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Contracts_read_canonical_camel_case_json()
    {
        var payload = """
            {
              "eventId": "11111111-1111-1111-1111-111111111111",
              "videoId": "22222222-2222-2222-2222-222222222222",
              "userId": "user-1",
              "errorCode": "PROCESSING_FAILED",
              "errorMessage": "ffmpeg failed",
              "occurredAt": "2026-08-23T13:30:00-03:00"
            }
            """;

        var failed = JsonSerializer.Deserialize<VideoProcessingFailed>(payload, JsonOptions);

        Assert.NotNull(failed);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), failed.EventId);
        Assert.Equal(TimeSpan.FromHours(-3), failed.OccurredAt.Offset);
        Assert.Equal(DateTimeOffset.Parse("2026-08-23T16:30:00Z"), failed.OccurredAt.ToUniversalTime());
        Assert.Contains("\"occurredAt\"", JsonSerializer.Serialize(failed, JsonOptions));
    }

    [Fact]
    public void Started_and_completed_contracts_round_trip_canonical_json()
    {
        var videoId = Guid.NewGuid();
        var started = new VideoProcessingStarted(Guid.NewGuid(), videoId, "user-1", DateTimeOffset.UtcNow);
        var completed = new VideoProcessingCompleted(
            Guid.NewGuid(),
            videoId,
            "user-1",
            $"results/user-1/{videoId}/resultado.zip",
            DateTimeOffset.UtcNow);

        var startedJson = JsonSerializer.Serialize(started, JsonOptions);
        var completedJson = JsonSerializer.Serialize(completed, JsonOptions);

        Assert.Contains("\"eventId\"", startedJson);
        Assert.Contains("\"resultObjectKey\"", completedJson);
        Assert.Equal(started, JsonSerializer.Deserialize<VideoProcessingStarted>(startedJson, JsonOptions));
        Assert.Equal(completed, JsonSerializer.Deserialize<VideoProcessingCompleted>(completedJson, JsonOptions));
    }
}
