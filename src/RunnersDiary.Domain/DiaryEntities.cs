using NetTopologySuite.Geometries;

namespace RunnersDiary.Domain;

public sealed class DiaryUser
{
    public required string Id { get; set; }
    public bool IsGuest { get; set; }
    public int GpxUploadRequestCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public string? DisplayName { get; set; }
}

public sealed class RunType
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public bool IsRace { get; set; }
}

public sealed class Run
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UserId { get; set; }
    public DateTimeOffset StartAtUtc { get; set; }
    public RunStatus Status { get; set; }
    public required string RunTypeCode { get; set; }
    public required string Title { get; set; }
    public string? Notes { get; set; }
    public double? DistanceMeters { get; set; }
    public int? DurationSeconds { get; set; }
    public double? ElevationGainMeters { get; set; }
    public double? ElevationLossMeters { get; set; }
    public int? AverageHeartRateBpm { get; set; }
    public int? MaximumHeartRateBpm { get; set; }
    public int? PerceivedEffort { get; set; }
    public string? ShoesOrGear { get; set; }
    public string? Weather { get; set; }
    public string? Surface { get; set; }
    public RunLocation? Location { get; set; }
    public RaceDetails? RaceDetails { get; set; }
    public RunRating? Rating { get; set; }
    public GpxTrack? GpxTrack { get; set; }
}

public enum RunStatus
{
    Planned,
    Completed
}

public sealed class RunLocation
{
    public required string DisplayName { get; set; }
    public required Point Coordinates { get; set; }
}

public sealed class RaceDetails
{
    public Guid RunId { get; set; }
    public string? RaceName { get; set; }
    public string? OfficialDistanceCategory { get; set; }
    public string? BibNumber { get; set; }
    public int? TargetTimeSeconds { get; set; }
    public int? ResultTimeSeconds { get; set; }
    public int? OverallPlace { get; set; }
    public int? CategoryPlace { get; set; }
    public string? ResultsUrl { get; set; }
}

public sealed class RunRating
{
    public int Stars { get; set; }
    public string? Comment { get; set; }
}

public sealed class GpxTrack
{
    public Guid RunId { get; set; }
    public required string BlobName { get; set; }
    public string? ProcessedBlobName { get; set; }
    public DateTimeOffset UploadedAtUtc { get; set; }
    public bool IsPlannedCourse { get; set; }
    public long FileSizeBytes { get; set; }
    public bool IsPendingDeletion { get; set; }
    public bool DeleteParentRun { get; set; }
}
