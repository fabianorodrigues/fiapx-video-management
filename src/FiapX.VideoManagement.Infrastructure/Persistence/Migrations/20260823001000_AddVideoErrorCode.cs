using FiapX.VideoManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiapX.VideoManagement.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VideoDbContext))]
[Migration("20260823001000_AddVideoErrorCode")]
public partial class AddVideoErrorCode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "error_code",
            table: "videos",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "error_code",
            table: "videos");
    }
}
