using FiapX.VideoManagement.Application.Videos;

namespace FiapX.VideoManagement.Api.Videos;

public static class VideoEndpoints
{
    public static IEndpointRouteBuilder MapVideoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/videos", async (
            CreateVideoRequest request,
            VideoService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.CreateAsync(request, cancellationToken);
            return Results.Created($"/videos/{response.VideoId}", response);
        });

        endpoints.MapGet("/videos", async (
            VideoService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.ListAsync(cancellationToken);
            return Results.Ok(response);
        });

        endpoints.MapGet("/videos/{videoId:guid}", async (
            Guid videoId,
            VideoService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.GetAsync(videoId, cancellationToken);
            return Results.Ok(response);
        });

        endpoints.MapGet("/videos/{videoId:guid}/download", async (
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
