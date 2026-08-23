using FiapX.VideoManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiapX.VideoManagement.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VideoDbContext))]
[Migration("20260823000000_CreateVideos")]
public partial class CreateVideos : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "videos",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                user_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                original_object_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                result_object_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                processing_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                processing_finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_videos", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_videos_user_created",
            table: "videos",
            columns: new[] { "user_id", "created_at" },
            descending: new[] { false, true });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "videos");
    }
}
