using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunnersDiary.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GuestUploadQuotaAndPendingTrackDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GpxUploadRequestCount",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "DeleteParentRun",
                table: "GpxTracks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPendingDeletion",
                table: "GpxTracks",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GpxUploadRequestCount",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DeleteParentRun",
                table: "GpxTracks");

            migrationBuilder.DropColumn(
                name: "IsPendingDeletion",
                table: "GpxTracks");
        }
    }
}
