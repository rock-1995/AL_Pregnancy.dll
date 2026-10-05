using BepInEx.Configuration;

namespace ALPregnancy;

internal static class PregnancyConfig
{
    private static ConfigFile _file;
    internal static event Action Changed;
    internal static int Revision { get; private set; }
    internal static string LastChange { get; private set; } = "Current F1 values are live";
    internal static ConfigEntry<bool> Gameplay;
    internal static ConfigEntry<ConceptionMode> Mode;
    internal static ConfigEntry<float> Fixed, Safe, Normal, Danger, Complex, Decay, Variation;
    internal static ConfigEntry<float> GrowthStart, GrowthEarlyEnd, GrowthMiddleEnd, GrowthLateStart;
    internal static ConfigEntry<int> NormalStart, NormalEnd, DangerStart, DangerEnd, Ovulation, Gestation, Recovery;
    internal static void Bind(ConfigFile config)
    {
        if (_file != null) _file.SettingChanged -= OnSettingChanged;
        Gameplay = config.Bind("Pregnancy", "Enable gameplay", true, "Enable conception and daily progression. Character state stays in memory until the game saves it to the companion file for the actual selected save slot. Disable to pause progression.");
        Mode = config.Bind("Pregnancy", "Conception mode", ConceptionMode.SimpleCycle, "SimpleCycle: a simple 28-day cycle (SVS style). ComplexCycle: a complex 28-day cycle (COM3D2 style). FixedChance: a fixed probability without a cycle.");
        ConfigEntry<float> Percent(string key, float value, string help) => config.Bind("Pregnancy probabilities", key, value, new ConfigDescription(help, new AcceptableValueRange<float>(0, 100)));
        Fixed = Percent("Fixed chance percent", 30, "Conception probability per event in FixedChance mode. 100 guarantees conception when not already pregnant or recovering.");
        Safe = Percent("Safe chance percent", 0, "Conception probability during the safe phase in SimpleCycle mode. This is a configurable gameplay rule.");
        Normal = Percent("Normal chance percent", 5, "Conception probability during the normal phase in SimpleCycle mode.");
        Danger = Percent("Danger chance percent", 50, "Conception probability during the fertile phase in SimpleCycle mode.");
        Complex = Percent("Complex base chance percent", 30, "Daily probability in ComplexCycle mode = this value x residual factor x ovulation factor. The ovulation factor is 1 on ovulation day, 0.5 on the following day, and 0 otherwise.");
        Decay = Percent("Residual retained percent", 75, "Percentage of the residual factor retained after each daily calculation in ComplexCycle mode. Values below 0.1 are cleared.");
        ConfigEntry<int> Day(string key, int value, string help) => config.Bind("Pregnancy cycle", key, value, new ConfigDescription(help, new AcceptableValueRange<int>(1, 28)));
        NormalStart = Day("Normal start", 8, "First day of the normal phase, inclusive. Ranges may wrap across the 28-day cycle boundary.");
        NormalEnd = Day("Normal end", 19, "Last day of the normal phase, inclusive.");
        DangerStart = Day("Danger start", 12, "First day of the fertile phase, inclusive. This phase takes precedence over the normal phase.");
        DangerEnd = Day("Danger end", 16, "Last day of the fertile phase, inclusive.");
        Ovulation = Day("Ovulation day", 14, "Ovulation day in ComplexCycle mode. The following day uses a factor of 0.5.");
        Gestation = config.Bind("Pregnancy progression", "Gestation days", 280, new ConfigDescription("Base gestation length in days. Standard daily growth is 1 / this value, with daily variation applied around that increment. Changes preserve normalized progress. Nighttime conception participates in the current daily advancement.", new AcceptableValueRange<int>(1, 1000)));
        Variation = config.Bind("Pregnancy progression", "Gestation variation percent", 0f, new ConfigDescription("Random daily growth variation, plus or minus this percentage. Each character rolls independently each day: increment = (1 / base days) x [1 - percentage, 1 + percentage]. For 4 days and 10%, daily growth is 0.225 to 0.275. Zero gives a fixed increment. Legacy random gestation lengths are no longer used; existing progress is preserved.", new AcceptableValueRange<float>(0, 100)));
        Recovery = config.Bind("Pregnancy progression", "Recovery days", 180, new ConfigDescription("Recovery length in days. Applies immediately while preserving normalized progress. Each game day adds 1 / days; recovery completes at 1. Zero disables recovery.", new AcceptableValueRange<int>(0, 1000)));
        ConfigEntry<float> Timing(string key, float value, string help, float maximum = 99) => config.Bind("Pregnancy belly timing", key, value,
            new ConfigDescription(help + " Values are percentages of gestation progress, independent of base days. Changes immediately adjust shape timing without changing pregnancy progress. Reversed or overlapping boundaries are ordered with a minimum gap of one percentage point. F8 shows the effective boundaries.", new AcceptableValueRange<float>(0, maximum)));
        GrowthStart = Timing("Growth start percent", 0, "Growth start. The belly does not grow before this point.", 96);
        GrowthEarlyEnd = Timing("Stage 1 end percent", 20, "End of stage 1, reaching the early belly shape. Stage 2 follows.", 97);
        GrowthMiddleEnd = Timing("Stage 2 end percent", 45, "End of stage 2, reaching the middle belly shape. Stage 3 follows.", 98);
        GrowthLateStart = Timing("Stage 3 end percent", 70, "End of stage 3 and start of the final growth stage. The final shape is reached at 100%, with linear growth within each stage.");
        _file = config;
        _file.SettingChanged += OnSettingChanged;
    }
    private static void OnSettingChanged(object sender, SettingChangedEventArgs args)
    {
        string section = args.ChangedSetting.Definition.Section;
        if (!section.StartsWith("Pregnancy", StringComparison.Ordinal) &&
            !(section == "General" && args.ChangedSetting.Definition.Key == "Enabled")) return;
        Revision++;
        LastChange = args.ChangedSetting.Definition.Key + " = " + args.ChangedSetting.BoxedValue;
        Changed?.Invoke();
    }
    internal static PregnancyRules Rules => new()
    {
        Mode = Mode.Value, FixedChance = Fixed.Value / 100d, SafeChance = Safe.Value / 100d,
        NormalChance = Normal.Value / 100d, DangerChance = Danger.Value / 100d, ComplexChance = Complex.Value / 100d,
        ResidualDecay = Decay.Value / 100d, NormalStart = NormalStart.Value, NormalEnd = NormalEnd.Value,
        DangerStart = DangerStart.Value, DangerEnd = DangerEnd.Value, OvulationDay = Ovulation.Value,
        GestationDays = Gestation.Value, GestationVariation = Variation.Value / 100d, RecoveryDays = Recovery.Value,
        GrowthStart = GrowthStart.Value / 100d, GrowthEarlyEnd = GrowthEarlyEnd.Value / 100d,
        GrowthMiddleEnd = GrowthMiddleEnd.Value / 100d, GrowthLateStart = GrowthLateStart.Value / 100d
    };
}
