using System.Globalization;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace RunnersDiary.Domain;

[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record TrackMetrics(double DistanceMeters, double ElevationGainMeters,
    double ElevationLossMeters, int? DurationSeconds, double StartLatitude, double StartLongitude);
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record ElevationSample(double DistanceMeters, double? ElevationMeters);
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record TrackGeometry(string Type, double[][][] Coordinates);
public sealed record TrackFeature(string Type, TrackGeometry Geometry, TrackMetrics Properties);
public sealed record ProcessedTrack(string Type, TrackFeature[] Features, ElevationSample[] Elevation,
    bool IsPlannedCourse);

public static class GpxParser
{
    public const int MaxBytes = 20 * 1024 * 1024;
    public const int MaxPoints = 100_000;
    private sealed record Point(double Lat, double Lon, double? Ele, DateTimeOffset? Time);

    public static ProcessedTrack Parse(Stream stream, bool planned)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaxBytes, IgnoreComments = true
        });
        var doc = XDocument.Load(reader);
        var root = doc.Root ?? throw new FormatException("Missing GPX root.");
        if (root.Name.LocalName != "gpx" ||
            root.Name.NamespaceName is not ("http://www.topografix.com/GPX/1/1" or "http://www.topografix.com/GPX/1/0"))
            throw new FormatException("Expected GPX 1.0 or 1.1.");
        var ns = root.Name.Namespace;
        var count = 0;
        var segments = new List<List<Point>>();
        foreach (var segment in root.Elements(ns + "trk").Elements(ns + "trkseg"))
        {
            var points = new List<Point>();
            foreach (var element in segment.Elements(ns + "trkpt"))
            {
                if (++count > MaxPoints) throw new FormatException("Too many track points.");
                var lat = Number(element.Attribute("lat")?.Value);
                var lon = Number(element.Attribute("lon")?.Value);
                if (lat is < -90 or > 90 || lon is < -180 or > 180)
                    throw new FormatException("Coordinate out of range.");
                var elevation = element.Element(ns + "ele");
                var ele = elevation is null ? (double?)null : Number(elevation.Value);
                if (ele is < -12000 or > 10000) throw new FormatException("Elevation out of range.");
                var timeText = element.Element(ns + "time")?.Value;
                var time = Timestamp(timeText);
                points.Add(new(lat, lon, ele, time));
            }
            if (points.Count > 0) segments.Add(points);
        }
        if (count == 0) throw new FormatException("No track points.");
        var all = segments.SelectMany(s => s).ToArray();
        double distance = 0, gain = 0, loss = 0;
        double? anchor = null;
        var profile = new List<ElevationSample>();
        var profileKeep = new SortedSet<int>();
        var display = new List<double[][]>();
        var work = 0;
        foreach (var segment in segments)
        {
            var keep = new SortedSet<int> { 0, segment.Count - 1 };
            double? shapeAnchor = null;
            for (var i = 0; i < segment.Count; i++)
            {
                var p = segment[i];
                if (i > 0) distance += Distance(segment[i - 1].Lat, segment[i - 1].Lon, p.Lat, p.Lon);
                if (p.Ele is double e)
                {
                    if (anchor is double a && Math.Abs(e - a) >= 3)
                    {
                        if (e > a) gain += e - a; else loss += a - e;
                        anchor = e;
                    }
                    anchor ??= e;
                    if (shapeAnchor is null || Math.Abs(e - shapeAnchor.Value) >= 3)
                    {
                        keep.Add(i);
                        if (i > 0) keep.Add(i - 1);
                        profileKeep.Add(profile.Count);
                        if (profile.Count > 0) profileKeep.Add(profile.Count - 1);
                        shapeAnchor = e;
                    }
                }
                profile.Add(new(distance, p.Ele));
            }
            // Iterative Douglas–Peucker in local metre space, retaining elevation anchors.
            var stack = new Stack<(int A, int B)>();
            var anchors = keep.ToArray();
            for (var i = 1; i < anchors.Length; i++) stack.Push((anchors[i - 1], anchors[i]));
            while (stack.TryPop(out var range))
            {
                var a = segment[range.A]; var b = segment[range.B];
                var scale = Math.Cos(a.Lat * Math.PI / 180);
                var bx = (b.Lon - a.Lon) * scale * 111195; var by = (b.Lat - a.Lat) * 111195;
                double max = 10; var index = -1;
                for (var i = range.A + 1; i < range.B; i++)
                {
                    if (++work > 5_000_000)
                    {
                        // Bound worst-case simplification CPU: retaining remaining points is deterministic and lossless.
                        for (var j = range.A + 1; j < range.B; j++) keep.Add(j);
                        break;
                    }
                    var x = (segment[i].Lon - a.Lon) * scale * 111195;
                    var y = (segment[i].Lat - a.Lat) * 111195;
                    var t = bx * bx + by * by == 0 ? 0 : Math.Clamp((x * bx + y * by) / (bx * bx + by * by), 0, 1);
                    var d = Math.Sqrt(Math.Pow(x - t * bx, 2) + Math.Pow(y - t * by, 2));
                    if (d > max) { max = d; index = i; }
                }
                if (index >= 0) { keep.Add(index); stack.Push((range.A, index)); stack.Push((index, range.B)); }
            }
            var coordinates = keep.Select(i => new[] { segment[i].Lon, segment[i].Lat }).ToList();
            if (coordinates.Count == 1) coordinates.Add(coordinates[0]);
            display.Add(coordinates.ToArray());
        }
        var ordered = all.All(p => p.Time.HasValue) &&
            all.Zip(all.Skip(1)).All(pair => pair.First.Time <= pair.Second.Time);
        var seconds = ordered ? (all[^1].Time!.Value - all[0].Time!.Value).TotalSeconds : (double?)null;
        var metrics = new TrackMetrics(distance, gain, loss,
            seconds is >= 0 and <= int.MaxValue ? (int)Math.Floor(seconds.Value) : null, all[0].Lat, all[0].Lon);
        // Regular sampling plus accepted elevation anchors preserves meaningful shape in the profile.
        var stride = Math.Max(1, (int)Math.Ceiling(profile.Count / 2000d));
        var reduced = profile.Where((_, i) => i % stride == 0 || i == profile.Count - 1 || profileKeep.Contains(i)).ToArray();
        return new("FeatureCollection", [new("Feature", new("MultiLineString", display.ToArray()), metrics)], reduced, planned);
    }

    private static double Number(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? number : throw new FormatException("Invalid numeric value.");

    private static DateTimeOffset? Timestamp(string? value)
    {
        if (value is null) return null;
        value = value.Trim();
        if (value.Length < 19 || value[10] != 'T') return null;
        // XML timestamps without an offset must not depend on the machine's time zone.
        if (!value.EndsWith('Z') && !value.AsSpan(19).Contains('+') && !value.AsSpan(19).Contains('-'))
            value += "Z";
        try { return XmlConvert.ToDateTimeOffset(value).ToUniversalTime(); }
        catch (FormatException) { return null; }
        catch (ArgumentException) { return null; }
    }

    public static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = Math.PI / 180;
        var a = Math.Pow(Math.Sin((lat2 - lat1) * r / 2), 2) +
            Math.Cos(lat1 * r) * Math.Cos(lat2 * r) * Math.Pow(Math.Sin((lon2 - lon1) * r / 2), 2);
        return 6371000 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }
}
