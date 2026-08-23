using FiapX.VideoManagement.Domain.Videos;
using Microsoft.EntityFrameworkCore;

namespace FiapX.VideoManagement.Infrastructure.Persistence;

public sealed class VideoDbContext : DbContext
{
    public VideoDbContext(DbContextOptions<VideoDbContext> options)
        : base(options)
    {
    }

    public DbSet<Video> Videos => Set<Video>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var video = modelBuilder.Entity<Video>();

        video.ToTable("videos");
        video.HasKey(v => v.Id);

        video.Property(v => v.Id).HasColumnName("id");
        video.Property(v => v.UserId).HasColumnName("user_id").HasMaxLength(100).IsRequired();
        video.Property(v => v.UserEmail).HasColumnName("user_email").HasMaxLength(255).IsRequired();
        video.Property(v => v.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255).IsRequired();
        video.Property(v => v.OriginalObjectKey).HasColumnName("original_object_key").HasMaxLength(500).IsRequired();
        video.Property(v => v.ResultObjectKey).HasColumnName("result_object_key").HasMaxLength(500);
        video.Property(v => v.Status)
            .HasColumnName("status")
            .HasMaxLength(30)
            .HasConversion(
                status => status.ToContractValue(),
                value => VideoStatusExtensions.FromContractValue(value))
            .IsRequired();
        video.Property(v => v.ErrorMessage).HasColumnName("error_message").HasMaxLength(1000);
        video.Property(v => v.CreatedAt).HasColumnName("created_at").IsRequired();
        video.Property(v => v.ProcessingStartedAt).HasColumnName("processing_started_at");
        video.Property(v => v.ProcessingFinishedAt).HasColumnName("processing_finished_at");
        video.Property(v => v.ErrorCode).HasColumnName("error_code").HasMaxLength(100);
        video.Property(v => v.Version).IsRowVersion();

        video.HasIndex(v => new { v.UserId, v.CreatedAt })
            .HasDatabaseName("ix_videos_user_created")
            .IsDescending(false, true);
    }
}
