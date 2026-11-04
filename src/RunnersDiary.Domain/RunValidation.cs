namespace RunnersDiary.Domain;

public static class RunValidation
{
    public static Dictionary<string, string[]> Validate(Run run, bool runTypeIsRace)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var messages))
            {
                messages = [];
                errors.Add(field, messages);
            }

            messages.Add(message);
        }

        if (string.IsNullOrWhiteSpace(run.Title) || run.Title.Length > 200)
        {
            Add(nameof(run.Title), "Title is required and must be at most 200 characters.");
        }

        if (run.StartAtUtc == default)
        {
            Add(nameof(run.StartAtUtc), "Start time is required.");
        }

        if (run.Notes?.Length > 4000)
        {
            Add(nameof(run.Notes), "Notes must be at most 4000 characters.");
        }

        if (run.ShoesOrGear?.Length > 200 || run.Weather?.Length > 100 || run.Surface?.Length > 50)
        {
            Add(nameof(run.Surface), "Shoes or gear, weather, and surface fields exceed their maximum lengths.");
        }

        if (run.DistanceMeters is { } distance
            && (!double.IsFinite(distance) || distance <= 0 || distance > 500_000))
        {
            Add(nameof(run.DistanceMeters), "Distance must be greater than zero and at most 500000 meters.");
        }

        if (run.DurationSeconds is <= 0 or > 604_800)
        {
            Add(nameof(run.DurationSeconds), "Duration must be between 1 second and 7 days.");
        }

        if (run.ElevationGainMeters is { } gain
                && (!double.IsFinite(gain) || gain < 0 || gain > 100_000)
            || run.ElevationLossMeters is { } loss
                && (!double.IsFinite(loss) || loss < 0 || loss > 100_000))
        {
            Add(nameof(run.ElevationGainMeters), "Elevation gain and loss must be between zero and 100000 meters.");
        }

        if (run.AverageHeartRateBpm is < 30 or > 250 || run.MaximumHeartRateBpm is < 30 or > 250)
        {
            Add(nameof(run.AverageHeartRateBpm), "Heart rate must be between 30 and 250 beats per minute.");
        }

        if (run.AverageHeartRateBpm is { } average
            && run.MaximumHeartRateBpm is { } maximum
            && maximum < average)
        {
            Add(nameof(run.MaximumHeartRateBpm), "Maximum heart rate cannot be lower than average heart rate.");
        }

        if (run.PerceivedEffort is < 1 or > 10)
        {
            Add(nameof(run.PerceivedEffort), "Perceived effort must be between 1 and 10.");
        }

        if (run.Status is not RunStatus.Planned and not RunStatus.Completed)
        {
            Add(nameof(run.Status), "Status must be Planned or Completed.");
        }

        if (run.Rating is not null)
        {
            if (run.Status != RunStatus.Completed)
            {
                Add(nameof(run.Rating), "A rating is allowed only for a completed run.");
            }

            if (run.Rating.Stars is < 1 or > 5)
            {
                Add(nameof(run.Rating), "Rating must be between 1 and 5 stars.");
            }

            if (run.Rating.Comment?.Length > 1000)
            {
                Add(nameof(run.Rating), "Rating comment must be at most 1000 characters.");
            }
        }

        if (run.RaceDetails is not null && !runTypeIsRace)
        {
            Add(nameof(run.RaceDetails), "Race details are allowed only for a race run type.");
        }

        if (run.RaceDetails is { } race)
        {
            if (race.TargetTimeSeconds is <= 0 || race.ResultTimeSeconds is <= 0)
            {
                Add(nameof(run.RaceDetails), "Race times must be greater than zero.");
            }

            if (race.OverallPlace is <= 0 || race.CategoryPlace is <= 0)
            {
                Add(nameof(run.RaceDetails), "Race places must be greater than zero.");
            }

            if (race.RaceName?.Length > 200
                || race.OfficialDistanceCategory?.Length > 100
                || race.BibNumber?.Length > 50
                || race.ResultsUrl?.Length > 2048)
            {
                Add(nameof(run.RaceDetails), "Race details exceed their maximum field lengths.");
            }

            if (race.ResultsUrl is not null
                && (!Uri.TryCreate(race.ResultsUrl, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            {
                Add(nameof(run.RaceDetails), "Results URL must be an absolute HTTP or HTTPS URL.");
            }
        }

        if (run.Location is { } location)
        {
            if (string.IsNullOrWhiteSpace(location.DisplayName)
                || location.DisplayName.Length > 250
                || !double.IsFinite(location.Coordinates.X)
                || !double.IsFinite(location.Coordinates.Y)
                || location.Coordinates.X is < -180 or > 180
                || location.Coordinates.Y is < -90 or > 90)
            {
                Add(nameof(run.Location), "Location must have a valid name, longitude, and latitude.");
            }
        }

        return errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }
}
