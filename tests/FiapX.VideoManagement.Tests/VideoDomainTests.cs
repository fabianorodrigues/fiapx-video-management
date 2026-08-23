using FiapX.VideoManagement.Domain.Common;
using FiapX.VideoManagement.Domain.Videos;

namespace FiapX.VideoManagement.Tests;

public sealed class VideoDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Register_creates_received_video()
    {
        var videoId = Guid.NewGuid();
        var video = Video.Register(
            videoId,
            "user-1",
            "user@fiapx.local",
            "video.mp4",
            VideoObjectKeys.Original("user-1", videoId),
            VideoObjectKeys.Result("user-1", videoId),
            Now);

        Assert.Equal(videoId, video.Id);
        Assert.Equal(VideoStatus.Recebido, video.Status);
        Assert.Equal("RECEBIDO", video.Status.ToContractValue());
        Assert.True(video.BelongsTo("user-1"));
    }

    [Fact]
    public void Transitions_follow_expected_flow()
    {
        var video = CreateVideo();

        video.MarkProcessing(Now.AddMinutes(1));
        video.MarkCompleted(VideoObjectKeys.Result(video.UserId, video.Id), Now.AddMinutes(2));

        Assert.Equal(VideoStatus.Concluido, video.Status);
        Assert.Equal(Now.AddMinutes(1), video.ProcessingStartedAt);
        Assert.Equal(Now.AddMinutes(2), video.ProcessingFinishedAt);
        video.EnsureCanDownload();
    }

    [Fact]
    public void Invalid_transitions_throw_domain_exception()
    {
        var video = CreateVideo();

        Assert.Throws<DomainException>(() =>
            video.MarkCompleted(VideoObjectKeys.Result(video.UserId, video.Id), Now));

        Assert.Throws<DomainException>(() =>
            video.MarkFailed("failed", Now));
    }

    [Fact]
    public void Download_is_only_available_when_completed()
    {
        var video = CreateVideo();

        Assert.Throws<DomainException>(video.EnsureCanDownload);

        video.MarkProcessing(Now.AddMinutes(1));
        video.MarkCompleted(VideoObjectKeys.Result(video.UserId, video.Id), Now.AddMinutes(2));

        video.EnsureCanDownload();
    }

    [Fact]
    public void Object_keys_follow_contract()
    {
        var videoId = Guid.Parse("b520d892-2591-4910-a79c-94fb01e24670");

        Assert.Equal(
            "videos/user-1/b520d892-2591-4910-a79c-94fb01e24670/original.mp4",
            VideoObjectKeys.Original("user-1", videoId));

        Assert.Equal(
            "results/user-1/b520d892-2591-4910-a79c-94fb01e24670/resultado.zip",
            VideoObjectKeys.Result("user-1", videoId));
    }

    private static Video CreateVideo()
    {
        var videoId = Guid.NewGuid();
        return Video.Register(
            videoId,
            "user-1",
            "user@fiapx.local",
            "video.mp4",
            VideoObjectKeys.Original("user-1", videoId),
            VideoObjectKeys.Result("user-1", videoId),
            Now);
    }
}
