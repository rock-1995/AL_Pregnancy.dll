using ALPregnancy;
using BepInEx.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class PregnancyLiveRegression
{
    internal static void Run(Action<string, bool> check)
    {
        static bool Near(double a, double b) => Math.Abs(a - b) < 1e-10;
        var directory = Path.Combine(AppContext.BaseDirectory, "live-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var config = new ConfigFile(Path.Combine(directory, "test.cfg"), false) { SaveOnConfigSet = false };
        PregnancyConfig.Bind(config);
        int notifications = 0;
        void Changed() => notifications++;
        PregnancyConfig.Changed += Changed;
        var world = new PregnancyWorld();
        var p = world.Register("live", "Live", 1);
        var random = new Random(14);
        PregnancyConfig.Mode.Value = ConceptionMode.FixedChance;
        PregnancyConfig.Fixed.Value = 0;
        check("F1 zero chance reaches the live simulation", !PregnancySimulation.Exposure(p, PregnancyConfig.Rules, "dad", "Dad", random));
        PregnancyConfig.Fixed.Value = 100;
        check("F1 probability notification and F8 model update immediately", notifications >= 3 && PregnancyConfig.LastChange.Contains("100") && PregnancyChance.Evaluate(p, PregnancyConfig.Rules).BaseChance == 1);
        check("F1 probability takes effect without reload or another day", PregnancySimulation.Exposure(p, PregnancyConfig.Rules, "dad", "Dad", random));
        var probability = PregnancyChance.Evaluate(p, PregnancyConfig.Rules);
        check("Pregnant actor displays configured chance plus blocked reason", probability.BaseChance == 1 && probability.EffectiveChance == 0 && probability.Reason == "Already pregnant");
        PregnancyDebugActions.Apply(world, world, p.Key, PregnancyDebugCommand.ClearPregnancy, 0, PregnancyConfig.Rules);
        PregnancyConfig.Mode.Value = ConceptionMode.ComplexCycle;
        PregnancyConfig.Complex.Value = 100;
        PregnancyConfig.Ovulation.Value = 14;
        p.CycleDay = 1; p.Residual = 1;
        probability = PregnancyChance.Evaluate(p, PregnancyConfig.Rules);
        check("Complex 100 percent still explains the zero ovulation coefficient", probability.BaseChance == 1 && probability.EggFactor == 0 && probability.EffectiveChance == 0 && probability.Reason.Contains("ovulation"));
        PregnancyConfig.Ovulation.Value = 1;
        check("F1 ovulation day applies to F8 immediately", PregnancyChance.Evaluate(p, PregnancyConfig.Rules).EffectiveChance == 1);
        p.Residual = 0;
        check("Zero residual has a distinct probability explanation", PregnancyChance.Evaluate(p, PregnancyConfig.Rules).Reason.Contains("No residual"));
        p.Residual = .5;
        check("F8 effective complex chance uses the same formula as simulation", PregnancyChance.Evaluate(p, PregnancyConfig.Rules).EffectiveChance == .5);
        check("Paused gameplay retains configured chance with zero effective chance", PregnancyChance.Evaluate(p, PregnancyConfig.Rules, false).BaseChance == 1 && PregnancyChance.Evaluate(p, PregnancyConfig.Rules, false).EffectiveChance == 0);
        PregnancyConfig.Mode.Value = ConceptionMode.SimpleCycle;
        PregnancyConfig.DangerStart.Value = 1; PregnancyConfig.DangerEnd.Value = 28; PregnancyConfig.Danger.Value = 37;
        check("F1 simple mode range and probability update together", Near(PregnancyChance.Evaluate(p, PregnancyConfig.Rules).EffectiveChance, .37));
        check("Live settings have not written any pregnancy save", Directory.GetFiles(directory).Length == 0);

        PregnancyConfig.Gestation.Value = 280;
        PregnancyConfig.Recovery.Value = 180;
        PregnancyDebugActions.Apply(world, world, p.Key, PregnancyDebugCommand.ForceConceive, 0, PregnancyConfig.Rules);
        p.PregnancyProgress = .5;
        float growth = p.Growth(40);
        PregnancyConfig.Gestation.Value = 4;
        check("Changing 280 to 4 days preserves progress and belly", p.PregnancyProgress == .5 && p.PregnancyDay(PregnancyConfig.Rules) == 2 && p.Growth(40) == growth);
        PregnancySimulation.NextDay(world, PregnancyConfig.Rules, random);
        check("Current duration controls next daily normalized increment", p.PregnancyProgress == .75 && p.IsPregnant);
        PregnancySimulation.NextDay(world, PregnancyConfig.Rules, random);
        check("Progress one births exactly once and starts normalized recovery", !p.IsPregnant && p.PregnancyProgress == 0 && p.Births == 1 && p.RecoveryProgress == 0);
        PregnancyConfig.Recovery.Value = 4;
        PregnancySimulation.NextDay(world, PregnancyConfig.Rules, random);
        check("Existing recovery immediately uses changed F1 duration", p.RecoveryProgress == .25 && p.RecoveryRemainingDays(PregnancyConfig.Rules) == 3);
        PregnancyConfig.Recovery.Value = 2;
        check("Recovery setting preserves completed fraction", p.RecoveryProgress == .25 && !p.CanConceive(PregnancyConfig.Rules));
        PregnancySimulation.NextDay(world, PregnancyConfig.Rules, random);
        check("Recovery daily increment is one divided by current duration", p.RecoveryProgress == .75);
        PregnancySimulation.NextDay(world, PregnancyConfig.Rules, random);
        check("Recovery completes at one without duplicate birth", p.RecoveryProgress == 1 && p.CanConceive(PregnancyConfig.Rules) && p.Births == 1);
        PregnancyDebugActions.Apply(world, world, p.Key, PregnancyDebugCommand.ForceConceive, 0, PregnancyConfig.Rules);
        PregnancyDebugActions.Apply(world, world, p.Key, PregnancyDebugCommand.SetDay, 1.5, PregnancyConfig.Rules);
        check("Decimal debug day converts through current duration", p.PregnancyProgress == .375);
        PregnancyDebugActions.Apply(world, world, p.Key, PregnancyDebugCommand.SetProgress, .5, PregnancyConfig.Rules);
        PregnancyConfig.Gestation.Value = 3;
        check("Normalized midpoint stays exact with an odd duration", p.PregnancyProgress == .5 && p.PregnancyDay(PregnancyConfig.Rules) == 1.5);
        check("Very short pregnancy still shows a belly before full term", p.Growth(40) > 0 && p.Growth(40) < 1);
        check("NaN and infinity debug inputs are rejected", !PregnancyDebugActions.Apply(world, world, p.Key, PregnancyDebugCommand.SetProgress, double.NaN, PregnancyConfig.Rules) && !PregnancyDebugActions.Apply(world, world, p.Key, PregnancyDebugCommand.SetDay, double.PositiveInfinity, PregnancyConfig.Rules));
        PregnancyConfig.Gestation.Value = 10;
        p.GestationVariationSample = .75;
        PregnancyConfig.Variation.Value = 10;
        check("F1 variation adjusts the daily increment range, not a fixed duration", p.GestationDays(PregnancyConfig.Rules) == 10 && Near(PregnancyConfig.Rules.DailyIncrement(0), .09) && Near(PregnancyConfig.Rules.DailyIncrement(1), .11));
        PregnancyConfig.Gestation.Value = 20;
        check("Changing duration rescales the next daily increment without changing progress", p.GestationDays(PregnancyConfig.Rules) == 20 && Near(PregnancyConfig.Rules.DailyIncrement(1), .055) && p.GestationVariationSample == .75 && p.PregnancyProgress == .5);
        PregnancyConfig.Variation.Value = 0;
        check("Zero percent variation immediately uses the exact configured duration", p.GestationDays(PregnancyConfig.Rules) == 20 && p.GestationVariationSample == .75);
        PregnancyDebugActions.Apply(world, world, p.Key, PregnancyDebugCommand.ClearPregnancy, 0, PregnancyConfig.Rules);
        p.RecoveryProgress = .25; p.Residual = 1; p.CycleDay = 1;
        PregnancyConfig.Mode.Value = ConceptionMode.ComplexCycle; PregnancyConfig.Recovery.Value = 0;
        check("Zero recovery duration enables conception immediately", p.CanConceive(PregnancyConfig.Rules) && PregnancyChance.Evaluate(p, PregnancyConfig.Rules).EffectiveChance == 1);
        PregnancySimulation.NextDay(world, PregnancyConfig.Rules, random);
        check("Complex simulation honors disabled recovery on the same day", p.IsPregnant && p.RecoveryProgress == 1);

        var roundingRules = new PregnancyRules { GestationDays = 280, RecoveryDays = 180 };
        var roundingWorld = new PregnancyWorld(); var r = roundingWorld.Register("round", "Round", 1);
        r.Pregnant = true;
        for (int i = 0; i < 279; i++) PregnancySimulation.NextDay(roundingWorld, roundingRules, random);
        check("Floating point accumulation never causes an early birth", r.IsPregnant && r.PregnancyProgress < 1);
        PregnancySimulation.NextDay(roundingWorld, roundingRules, random);
        check("Floating point accumulation never delays day 280 birth", !r.IsPregnant && r.Births == 1 && r.RecoveryProgress == 0);
        for (int i = 0; i < 180; i++) PregnancySimulation.NextDay(roundingWorld, roundingRules, random);
        check("Normalized recovery completes exactly at configured day 180", r.RecoveryProgress == 1);
        r.Pregnant = true; r.PregnancyProgress = .8; roundingRules.GestationDays = 1;
        PregnancySimulation.NextDay(roundingWorld, roundingRules, random);
        check("One-day gestation cannot overflow progress or repeat birth", r.Births == 2 && !r.IsPregnant && r.RecoveryProgress == 0);

        var legacy = new JsonObject
        {
            ["Version"] = 1, ["WorldId"] = "legacy-test", ["ElapsedDays"] = 10,
            ["CardCycles"] = new JsonObject { ["card"] = 18 },
            ["Characters"] = new JsonObject
            {
                ["old"] = new JsonObject { ["Key"] = "old", ["Name"] = "Old", ["CycleDay"] = 18, ["PregnantDays"] = 150, ["DueDays"] = 300, ["RecoveryDays"] = 0, ["FatherKey"] = "dad", ["FatherName"] = "Dad", ["Births"] = 2 },
                ["recover"] = new JsonObject { ["Key"] = "recover", ["CycleDay"] = 7, ["PregnantDays"] = -1, ["DueDays"] = 280, ["RecoveryDays"] = 90, ["Births"] = 1 }
            }
        };
        string slot = Path.Combine(directory, "legacy.sav"); File.WriteAllText(slot, "legacy native");
        var envelope = new JsonObject { ["Version"] = 1, ["NativeHash"] = PregnancyStorage.Hash(slot), ["World"] = legacy };
        File.WriteAllText(PregnancyStorage.Sidecar(slot), envelope.ToJsonString());
        string beforeHash = PregnancyStorage.Hash(PregnancyStorage.Sidecar(slot));
        var migrationRules = new PregnancyRules { RecoveryDays = 180, GestationDays = 280 };
        var migrated = PregnancyStorage.Load(slot, "", out _, migrationRules);
        var old = migrated.Characters["old"];
        check("Legacy days migrate to normalized pregnancy preserving identity/history", migrated.Version == 3 && old.Pregnant && old.PregnancyProgress == .5 && old.FatherName == "Dad" && old.Births == 2 && Near(migrated.CardCycleProgress["card"], 17d / 28));
        double oldT = (150d - 40) / (300 - 40);
        check("Legacy migration preserves the preexisting belly growth curve", Math.Abs(old.Growth(40) - oldT * oldT * (3 - 2 * oldT)) < 1e-6);
        check("Legacy remaining recovery migrates using current configured length", migrated.Characters["recover"].RecoveryProgress == .5);
        check("Legacy migration remains memory-only until explicit save", PregnancyStorage.Hash(PregnancyStorage.Sidecar(slot)) == beforeHash && !File.Exists(PregnancyStorage.Sidecar(slot) + ".bak"));
        string serialized = JsonSerializer.Serialize(migrated);
        check("Normalized save contains no legacy day counters", !serialized.Contains("PregnantDays") && !serialized.Contains("DueDays") && !serialized.Contains("RecoveryDays") && serialized.Contains("PregnancyProgress"));
        string slotB = Path.Combine(directory, "migrated.sav");
        var request = new PregnancySaveRequest(slotB, migrated);
        old.PregnancyProgress = .9;
        File.WriteAllText(slotB, "native B"); request.Commit(PregnancyStorage.Hash(slotB), "");
        check("Migrated snapshot is frozen and saved only to selected B", PregnancyStorage.Load(slotB, "", out _).Characters["old"].PregnancyProgress == .5 && PregnancyStorage.Hash(PregnancyStorage.Sidecar(slot)) == beforeHash);
        var invalid = migrated.Clone(); invalid.Characters["old"].PregnancyProgress = 1.01;
        bool rejected = false; try { invalid.Validate(); } catch (InvalidDataException) { rejected = true; }
        check("Out-of-range normalized save is rejected", rejected);
        legacy["Characters"]["old"]["PregnantDays"] = 2; legacy["Characters"]["old"]["DueDays"] = 4;
        legacy["Characters"]["recover"]["RecoveryDays"] = 500;
        using var shortJson = JsonDocument.Parse(legacy.ToJsonString());
        var shortWorld = PregnancyMigration.Read(shortJson.RootElement, migrationRules);
        check("Legacy short gestation gains proportional growth instead of waiting 40 days", shortWorld.Characters["old"].PregnancyProgress == .5 && shortWorld.Characters["old"].Growth(40) > 0);
        check("Unknown legacy recovery length preserves an active recovery", shortWorld.Characters["recover"].RecoveryProgress == 0);
        PregnancyConfig.Changed -= Changed;
        string oldConfigPath = Path.Combine(directory, "old.cfg");
        File.WriteAllText(oldConfigPath, "[Pregnancy progression]\nProgression speed = 280\nGestation days = 4\nGestation variation = 11\nRecovery days = 4\n");
        var oldConfig = new ConfigFile(oldConfigPath, false) { SaveOnConfigSet = false };
        PregnancyConfig.Bind(oldConfig);
        check("Removed speed setting is not registered in the F1 settings list", !oldConfig.Keys.Any(x => x.Key == "Progression speed"));
        check("Legacy variation days no longer register and cannot shorten four days", !oldConfig.Keys.Any(x => x.Key == "Gestation variation") && PregnancyConfig.Variation.Value == 0);
        var noSpeedWorld = new PregnancyWorld(); var noSpeed = noSpeedWorld.Register("no-speed", "No Speed", 1);
        noSpeed.Pregnant = true;
        PregnancySimulation.NextDay(noSpeedWorld, PregnancyConfig.Rules, random);
        check("Legacy speed 280 cannot accelerate pregnancy", noSpeed.PregnancyProgress == .25 && noSpeed.IsPregnant);
        noSpeed.Pregnant = false; noSpeed.PregnancyProgress = 0; noSpeed.RecoveryProgress = 0;
        PregnancySimulation.NextDay(noSpeedWorld, PregnancyConfig.Rules, random);
        check("Legacy speed 280 cannot accelerate recovery", noSpeed.RecoveryProgress == .25);
    }
}

