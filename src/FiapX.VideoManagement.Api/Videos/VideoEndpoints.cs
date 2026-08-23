using FiapX.VideoManagement.Application.Videos;

namespace FiapX.VideoManagement.Api.Videos;

public static class VideoEndpoints
{
    public static IEndpointRouteBuilder MapVideoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var videos = endpoints.MapGroup("/videos").RequireAuthorization();

        videos.MapPost("", async (
            CreateVideoRequest request,
            VideoService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.CreateAsync(request, cancellationToken);
            return Results.Created($"/videos/{response.VideoId}", response);
        });

        videos.MapGet("", async (
            VideoService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.ListAsync(cancellationToken);
            return Results.Ok(response);
        });

        videos.MapGet("/{videoId:guid}", async (
            Guid videoId,
            VideoService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.GetAsync(videoId, cancellationToken);
            return Results.Ok(response);
        });

        videos.MapGet("/{videoId:guid}/download", async (
            Guid videoId,
            VideoService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.GetDownloadAsync(videoId, cancellationToken);
            return Results.Ok(response);
        });

        return endpoints;
    }
}
