using FiapX.VideoManagement.Application.Videos;

namespace FiapX.VideoManagement.Api.Endpoints;

public static class VideoEndpoints
{
    public static IEndpointRouteBuilder MapVideoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var videos = endpoints.MapGroup("/videos").RequireAuthorization();

        videos.MapPost("", async (
            CreateVideoRequest request,
            ServicoVideo service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.CriarAsync(request, cancellationToken);
            return Results.Created($"/videos/{response.VideoId}", response);
        });

        videos.MapGet("", async (
            ServicoVideo service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.ListarAsync(cancellationToken);
            return Results.Ok(response);
        });

        videos.MapGet("/{videoId:guid}", async (
            Guid videoId,
            ServicoVideo service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.ObterAsync(videoId, cancellationToken);
            return Results.Ok(response);
        });

        videos.MapGet("/{videoId:guid}/download", async (
            Guid videoId,
            ServicoVideo service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.ObterDownloadAsync(videoId, cancellationToken);
            return Results.Ok(response);
        });

        return endpoints;
    }
}
