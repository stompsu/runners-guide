using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace RunnersDiary.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialDiarySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunTypes",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsRace = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunTypes", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsGuest = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RunTypeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DistanceMeters = table.Column<double>(type: "float", nullable: true),
                    DurationSeconds = table.Column<int>(type: "int", nullable: true),
                    ElevationGainMeters = table.Column<double>(type: "float", nullable: true),
                    ElevationLossMeters = table.Column<double>(type: "float", nullable: true),
                    AverageHeartRateBpm = table.Column<int>(type: "int", nullable: true),
                    MaximumHeartRateBpm = table.Column<int>(type: "int", nullable: true),
                    PerceivedEffort = table.Column<int>(type: "int", nullable: true),
                    ShoesOrGear = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Weather = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Surface = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Location_DisplayName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Location_Coordinates = table.Column<Point>(type: "geography", nullable: true),
                    Rating_Stars = table.Column<int>(type: "int", nullable: true),
                    Rating_Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Runs_RunTypes_RunTypeCode",
                        column: x => x.RunTypeCode,
                        principalTable: "RunTypes",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Runs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GpxTracks",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlobName = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsPlannedCourse = table.Column<bool>(type: "bit", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GpxTracks", x => x.RunId);
                    table.ForeignKey(
                        name: "FK_GpxTracks_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RaceDetails",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RaceName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OfficialDistanceCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BibNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TargetTimeSeconds = table.Column<int>(type: "int", nullable: true),
                    ResultTimeSeconds = table.Column<int>(type: "int", nullable: true),
                    OverallPlace = table.Column<int>(type: "int", nullable: true),
                    CategoryPlace = table.Column<int>(type: "int", nullable: true),
                    ResultsUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaceDetails", x => x.RunId);
                    table.ForeignKey(
                        name: "FK_RaceDetails_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "RunTypes",
                columns: new[] { "Code", "Category", "IsRace", "Name" },
                values: new object[,]
                {
                    { "cross-country", "Race", true, "Cross-country" },
                    { "cross-training", "Other", false, "Cross-training" },
                    { "easy-run", "Training", false, "Easy run" },
                    { "fartlek", "Training", false, "Fartlek" },
                    { "hill-repeats", "Training", false, "Hill repeats" },
                    { "intervals", "Training", false, "Intervals" },
                    { "long-run", "Training", false, "Long run" },
                    { "progression-run", "Training", false, "Progression run" },
                    { "race-pace-run", "Training", false, "Race-pace run" },
                    { "recovery-run", "Training", false, "Recovery run" },
                    { "rest-injury", "Other", false, "Rest / injury day" },
                    { "road-race", "Race", true, "Road race" },
                    { "strides-drills", "Training", false, "Strides / drills" },
                    { "tempo-threshold", "Training", false, "Tempo / Threshold" },
                    { "track-race", "Race", true, "Track race" },
                    { "trail-race", "Race", true, "Trail race" },
                    { "trail-training", "Training", false, "Trail training" },
                    { "treadmill-run", "Training", false, "Treadmill run" },
                    { "ultra-road", "Race", true, "Ultra (road)" },
                    { "ultra-trail", "Race", true, "Ultra (trail)" },
                    { "virtual-race", "Race", true, "Virtual race" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_RunTypeCode",
                table: "Runs",
                column: "RunTypeCode");

            migrationBuilder.CreateIndex(
                name: "IX_Runs_UserId_StartAtUtc",
                table: "Runs",
                columns: new[] { "UserId", "StartAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_ExpiresAtUtc",
                table: "Users",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GpxTracks");

            migrationBuilder.DropTable(
                name: "RaceDetails");

            migrationBuilder.DropTable(
                name: "Runs");

            migrationBuilder.DropTable(
                name: "RunTypes");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
