namespace RunnersDiary.Domain;

public static class RunTypeCatalog
{
    public static IReadOnlyList<RunType> All { get; } =
    [
        new() { Code = "road-race", Name = "Road race", Category = "Race", IsRace = true },
        new() { Code = "trail-race", Name = "Trail race", Category = "Race", IsRace = true },
        new() { Code = "ultra-road", Name = "Ultra (road)", Category = "Race", IsRace = true },
        new() { Code = "ultra-trail", Name = "Ultra (trail)", Category = "Race", IsRace = true },
        new() { Code = "track-race", Name = "Track race", Category = "Race", IsRace = true },
        new() { Code = "cross-country", Name = "Cross-country", Category = "Race", IsRace = true },
        new() { Code = "virtual-race", Name = "Virtual race", Category = "Race", IsRace = true },
        new() { Code = "easy-run", Name = "Easy run", Category = "Training" },
        new() { Code = "recovery-run", Name = "Recovery run", Category = "Training" },
        new() { Code = "long-run", Name = "Long run", Category = "Training" },
        new() { Code = "tempo-threshold", Name = "Tempo / Threshold", Category = "Training" },
        new() { Code = "intervals", Name = "Intervals", Category = "Training" },
        new() { Code = "fartlek", Name = "Fartlek", Category = "Training" },
        new() { Code = "hill-repeats", Name = "Hill repeats", Category = "Training" },
        new() { Code = "progression-run", Name = "Progression run", Category = "Training" },
        new() { Code = "race-pace-run", Name = "Race-pace run", Category = "Training" },
        new() { Code = "strides-drills", Name = "Strides / drills", Category = "Training" },
        new() { Code = "trail-training", Name = "Trail training", Category = "Training" },
        new() { Code = "treadmill-run", Name = "Treadmill run", Category = "Training" },
        new() { Code = "cross-training", Name = "Cross-training", Category = "Other" },
        new() { Code = "rest-injury", Name = "Rest / injury day", Category = "Other" }
    ];
}
