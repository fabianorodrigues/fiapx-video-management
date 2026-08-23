using System.Diagnostics;
using FiapX.VideoManagement.Api.Videos;
using FiapX.VideoManagement.Application.Common;
using FiapX.VideoManagement.Domain.Common;
using FiapX.VideoManagement.Infrastructure;
using FiapX.VideoManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddVideoManagementInfrastructure(builder.Configuration);

var app = builder.Build();

if (args.Any(arg => string.Equals(arg, "--migrate", StringComparison.OrdinalIgnoreCase)))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<VideoDbContext>();
    await dbContext.Database.MigrateAsync();
    return;
}

app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (Exception exception) when (exception is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
    {
        await WriteProblemDetailsAsync(context, exception);
    }
});

app.MapHealthChecks("/health");
app.MapVideoEndpoints();

await app.RunAsync();

static async Task WriteProblemDetailsAsync(HttpContext context, Exception exception)
{
    if (context.Response.HasStarted)
    {
        throw exception;
    }

    var statusCode = exception switch
    {
        RequestValidationException or DomainException or BadHttpRequestException => StatusCodes.Status400BadRequest,
        ResourceNotFoundException => StatusCodes.Status404NotFound,
        ResourceConflictException => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError
    };

    if (statusCode >= StatusCodes.Status500InternalServerError)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ProblemDetails");
        logger.LogError(exception, "Unhandled request error.");
    }

    var title = statusCode switch
    {
        StatusCodes.Status400BadRequest => "Invalid request.",
        StatusCodes.Status404NotFound => "Resource not found.",
        StatusCodes.Status409Conflict => "Request conflict.",
        _ => "Unexpected error."
    };

    context.Response.Clear();
    var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

    await Results.Problem(
        statusCode: statusCode,
        title: title,
        detail: exception.Message,
        extensions: new Dictionary<string, object?> { ["traceId"] = traceId })
        .ExecuteAsync(context);
}

public partial class Program
{
}
