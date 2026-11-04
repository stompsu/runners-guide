using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunnersDiary.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TrackProcessedArtifact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProcessedBlobName",
                table: "GpxTracks",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessedBlobName",
                table: "GpxTracks");
        }
    }
}
