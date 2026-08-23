using Microsoft.Extensions.Configuration;

namespace FiapX.VideoManagement.Infrastructure.Storage;

public sealed class S3StorageOptions
{
    public required string InternalEndpoint { get; init; }
    public required string PublicEndpoint { get; init; }
    public required string AccessKey { get; init; }
    public required string SecretKey { get; init; }
    public required string BucketName { get; init; }
    public string Region { get; init; } = "us-east-1";
    public int PresignedUrlExpiresSeconds { get; init; } = 900;

    public static S3StorageOptions FromConfiguration(IConfiguration configuration) =>
        new()
        {
            InternalEndpoint = configuration["MINIO_INTERNAL_ENDPOINT"] ?? "http://localhost:9000",
            PublicEndpoint = configuration["MINIO_PUBLIC_ENDPOINT"] ?? "http://localhost:9000",
            AccessKey = configuration["MINIO_ACCESS_KEY"] ?? "fiapx-dev",
            SecretKey = configuration["MINIO_SECRET_KEY"] ?? "fiapx-dev-password",
            BucketName = configuration["MINIO_BUCKET"] ?? "videos",
            Region = configuration["MINIO_REGION"] ?? "us-east-1",
            PresignedUrlExpiresSeconds = configuration.GetValue("PRESIGNED_URL_EXPIRES_SECONDS", 900)
        };
}
