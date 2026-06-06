namespace JobRoulettePlugin;

public enum RouletteType
{
    Leveling,
    Trials,
    MainScenario,
    AllianceRaid,
    NormalRaid,
    Mentor,
    Expert,
    HighLevelDungeons,
    LevelCapDungeons,
    Guildhests,
}

public static class RouletteCatalog
{
    private static readonly RouletteDefinition[] RouletteDefinitions =
    [
        new(RouletteType.Leveling, "Duty Roulette: Leveling", ["leveling", "level", "ldr"]),
        new(RouletteType.Trials, "Duty Roulette: Trials", ["trial", "trials"]),
        new(RouletteType.MainScenario, "Duty Roulette: Main Scenario", ["mainscenario", "main", "msq", "ms"]),
        new(RouletteType.AllianceRaid, "Duty Roulette: Alliance Raids", ["alliance", "allianceraid", "allianceraids", "araid", "ally"]),
        new(RouletteType.NormalRaid, "Duty Roulette: Normal Raids", ["normal", "normalraid", "normalraids", "nraid"]),
        new(RouletteType.Mentor, "Duty Roulette: Mentor", ["mentor"]),
        new(RouletteType.Expert, "Duty Roulette: Expert", ["expert", "ex", "exdr"]),
        new(RouletteType.HighLevelDungeons, "Duty Roulette: High-level Dungeons", ["highlevel", "high", "hl"]),
        new(RouletteType.LevelCapDungeons, "Duty Roulette: Level Cap Dungeons", ["levelcap", "levelcapdungeons", "cap"]),
        new(RouletteType.Guildhests, "Duty Roulette: Guildhests", ["guildhest", "guildhests"]),
    ];

    public static IReadOnlyList<RouletteDefinition> All { get; } = RouletteDefinitions;

    public static IReadOnlyDictionary<string, RouletteDefinition> Aliases { get; } = RouletteDefinitions
        .SelectMany(definition => definition.Aliases.Select(alias => new KeyValuePair<string, RouletteDefinition>(alias, definition)))
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string token, out RouletteDefinition definition)
        => Aliases.TryGetValue(token, out definition!);
}

public sealed record RouletteDefinition(RouletteType Type, string DisplayName, IReadOnlyList<string> Aliases);
