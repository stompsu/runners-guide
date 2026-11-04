using System.Text;
using RunnersDiary.Domain;

public sealed class GpxParserTests
{
    [Fact]
    public void Synthetic_demo_elevated_track_has_expected_metrics()
    {
        using var stream = typeof(RunnersDiary.Infrastructure.DemoDataSeeder).Assembly
            .GetManifestResourceStream("RunnersDiary.Infrastructure.Demo.synthetic-silesia.gpx")!;
        var track = GpxParser.Parse(stream, true);
        var metrics = track.Features[0].Properties;
        Assert.True(track.IsPlannedCourse);
        Assert.Equal(360, metrics.DurationSeconds);
        Assert.Equal(20, metrics.ElevationGainMeters);
        Assert.Equal(20, metrics.ElevationLossMeters);
        Assert.InRange(metrics.DistanceMeters, 1400, 2200);
    }
    private static ProcessedTrack Parse(string xml) => GpxParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)), false);
    private static string Wrap(string points) => $"""<gpx xmlns="http://www.topografix.com/GPX/1/1"><trk><trkseg>{points}</trkseg></trk></gpx>""";

    [Fact]
    public void Haversine_timestamps_noise_and_missing_elevation_are_deterministic()
    {
        var xml = Wrap("""
            <trkpt lat="0" lon="0"><ele>100</ele><time>2026-01-01T00:00:00Z</time></trkpt>
            <trkpt lat="0" lon="0.001"><ele>102</ele><time>2026-01-01T00:01:00Z</time></trkpt>
            <trkpt lat="0" lon="0.002"><time>2026-01-01T00:02:00Z</time></trkpt>
            <trkpt lat="0" lon="0.003"><ele>103</ele><time>2026-01-01T00:03:00Z</time></trkpt>
            <trkpt lat="0" lon="0.004"><ele>101</ele><time>2026-01-01T00:04:00Z</time></trkpt>
            <trkpt lat="0" lon="0.005"><ele>99</ele><time>2026-01-01T00:05:00Z</time></trkpt>
            """);
        var a = Parse(xml);
        var m = a.Features[0].Properties;
        Assert.InRange(m.DistanceMeters, 555.97, 555.98);
        Assert.Equal(3, m.ElevationGainMeters);
        Assert.Equal(4, m.ElevationLossMeters);
        Assert.Equal(300, m.DurationSeconds);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(a), System.Text.Json.JsonSerializer.Serialize(Parse(xml)));
        Assert.Null(Parse(xml.Replace("<time>2026-01-01T00:02:00Z</time>", "", StringComparison.Ordinal)).Features[0].Properties.DurationSeconds);
        Assert.Null(Parse(xml.Replace("00:02:00Z", "00:00:00Z", StringComparison.Ordinal)).Features[0].Properties.DurationSeconds);
        Assert.Null(Parse(xml.Replace("00:02:00Z", "invalid", StringComparison.Ordinal)).Features[0].Properties.DurationSeconds);
        Assert.Null(Parse(xml.Replace("2026-01-01T00:02:00Z", "2026-01-01", StringComparison.Ordinal)).Features[0].Properties.DurationSeconds);
        Assert.Equal(300, Parse(xml.Replace("Z", "", StringComparison.Ordinal)).Features[0].Properties.DurationSeconds);
    }

    [Theory]
    [InlineData("<gpx>")]
    [InlineData("<!DOCTYPE gpx [<!ENTITY x SYSTEM 'file:///never-read'>]><gpx>&x;</gpx>")]
    [InlineData("<!DOCTYPE gpx><gpx/>")]
    [InlineData("<gpx/>")]
    public void Invalid_xml_and_doctypes_are_rejected(string xml) =>
        Assert.ThrowsAny<Exception>(() => Parse(xml));

    [Theory]
    [InlineData("")]
    [InlineData("<trkpt lat='NaN' lon='18'/>")]
    [InlineData("<trkpt lat='50' lon='Infinity'/>")]
    [InlineData("<trkpt lat='91' lon='18'/>")]
    [InlineData("<trkpt lat='50' lon='181'/>")]
    [InlineData("<trkpt lat='50'/>")]
    [InlineData("<trkpt lat='50' lon='18'><ele>NaN</ele></trkpt>")]
    public void Bad_track_content_is_rejected(string points) =>
        Assert.Throws<FormatException>(() => Parse(Wrap(points)));

    [Fact]
    public void Unreasonable_point_count_is_rejected() =>
        Assert.Throws<FormatException>(() => Parse(Wrap(string.Concat(
            Enumerable.Repeat("<trkpt lat='50' lon='18'/>", GpxParser.MaxPoints + 1)))));

    [Fact]
    public void Straight_track_simplifies_but_keeps_endpoints_and_hill_shape()
    {
        var points = Enumerable.Range(0, 1000).Select(i =>
            $"<trkpt lat='0' lon='{(i * .00001).ToString(System.Globalization.CultureInfo.InvariantCulture)}'><ele>{(i == 500 ? 120 : 100)}</ele></trkpt>");
        var track = Parse(Wrap(string.Concat(points)));
        var geometry = track.Features[0].Geometry.Coordinates[0];
        Assert.InRange(geometry.Length, 3, 10);
        Assert.Equal(0, geometry[0][0]);
        Assert.Equal(.00999, geometry[^1][0], 8);
        Assert.Contains(geometry, p => Math.Abs(p[0] - .005) < .0000001);
        Assert.Equal(20, track.Features[0].Properties.ElevationGainMeters);
        Assert.Equal(20, track.Features[0].Properties.ElevationLossMeters);
    }

    [Fact]
    public void Segments_are_not_joined_for_distance_or_geometry()
    {
        var track = Parse("""<gpx xmlns="http://www.topografix.com/GPX/1/1"><trk><trkseg><trkpt lat="0" lon="0"/></trkseg><trkseg><trkpt lat="50" lon="18"/></trkseg></trk></gpx>""");
        Assert.Equal(0, track.Features[0].Properties.DistanceMeters);
        Assert.Equal(2, track.Features[0].Geometry.Coordinates.Length);
    }
}
