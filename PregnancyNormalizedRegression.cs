using ALPregnancy;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class PregnancyNormalizedRegression
{
    internal static void Run(Action<string, bool> check)
    {
        static bool Near(double a, double b) => Math.Abs(a - b) < 1e-10;
        var random = new Random(20261005);
        foreach (var mode in Enum.GetValues<ConceptionMode>())
        {
            var rules = new PregnancyRules { Mode = mode, FixedChance = 1, DangerChance = 1, ComplexChance = 1, GestationDays = 4, RecoveryDays = 4 };
            var world = new PregnancyWorld();
            var p = world.Register("four", "Four", 14);
            PregnancySimulation.Exposure(p, rules, "dad", "Dad", random);
            check(mode + " starts with zero progress on the event night", p.PregnancyProgress == 0 && p.Births == 0);
            for (int day = 1; day <= 3; day++)
            {
                PregnancySimulation.NextDay(world, rules, random);
                check(mode + " four days, morning " + day, p.IsPregnant && p.PregnancyProgress == day / 4d && p.Births == 0);
            }
            PregnancySimulation.NextDay(world, rules, random);
            check(mode + " birth on fourth advance only", !p.IsPregnant && p.Births == 1 && p.LastBirthDay == 4 && p.RecoveryProgress == 0);
            for (int day = 1; day <= 4; day++) PregnancySimulation.NextDay(world, rules, random);
            check(mode + " recovery lasts four advances after birth", p.RecoveryProgress == 1 && p.Births == 1);
        }

        var variableRules = new PregnancyRules { GestationDays = 4, GestationVariation = .1 };
        foreach (double sample in new[] { 0d, .5, 1d })
        {
            var world = new PregnancyWorld(); var p = world.Register("fraction", "Fraction", 1);
            p.Pregnant = true; p.GestationVariationSample = sample;
            double increment = (1 + (2 * sample - 1) * .1) / 4;
            var dailyRandom = new DailySequenceRandom(sample);
            check("Old fixed sample no longer alters configured duration " + sample, p.GestationDays(variableRules) == 4);
            PregnancySimulation.NextDay(world, variableRules, dailyRandom);
            check("Daily variation scales the standard daily increment " + sample, Near(p.PregnancyProgress, increment));
            for (int i = 1; i < Math.Ceiling(1 / increment) - 1; i++) PregnancySimulation.NextDay(world, variableRules, dailyRandom);
            check("Fractional pregnancy remains until the required day boundary " + sample, p.IsPregnant && p.PregnancyProgress < 1);
            PregnancySimulation.NextDay(world, variableRules, dailyRandom);
            check("Fractional pregnancy births once at completion " + sample, !p.IsPregnant && p.Births == 1);
        }

        var calendar = new PregnancyWorld(); var cycle = calendar.Register("cycle", "Cycle", 1);
        calendar.CardCycleProgress["card.png"] = 0;
        bool cycleCorrect = true;
        for (int day = 1; day <= 2800; day++)
        {
            PregnancySimulation.NextDay(calendar, new PregnancyRules(), random);
            cycleCorrect &= cycle.CycleDay == day % 28 + 1 && Near(cycle.CycleProgress, (day % 28) / 28d) &&
                Near(calendar.CardCycleProgress["card.png"], cycle.CycleProgress);
        }
        check("One hundred normalized cycles retain every day boundary and wrap", cycleCorrect);
        string normalized = JsonSerializer.Serialize(calendar);
        check("V3 saves contain normalized cycle fields and no day counters or signed roll", normalized.Contains("CycleProgress") && normalized.Contains("GestationVariationSample") && !normalized.Contains("CycleDay") && !normalized.Contains("CardCycles") && !normalized.Contains("VariationRoll"));

        var v2 = new JsonObject
        {
            ["Version"] = 2, ["WorldId"] = "v2", ["ElapsedDays"] = 5,
            ["CardCycles"] = new JsonObject { ["card.png"] = 28 },
            ["Characters"] = new JsonObject
            {
                ["old"] = new JsonObject { ["Key"] = "old", ["Name"] = "Old", ["CycleDay"] = 28, ["Pregnant"] = true,
                    ["PregnancyProgress"] = .5, ["RecoveryProgress"] = 1, ["GestationVariationRoll"] = -1, ["FatherName"] = "Dad", ["Births"] = 2 }
            }
        };
        using var json = JsonDocument.Parse(v2.ToJsonString());
        var migrated = PregnancyMigration.Read(json.RootElement, variableRules);
        var old = migrated.Characters["old"];
        check("V2 sample is retained as legacy data without changing the new base duration", old.GestationVariationSample == 0 && old.GestationDays(variableRules) == 4);
        check("V2 migration preserves progress, cycle, parent and birth history", old.PregnancyProgress == .5 && old.RecoveryProgress == 1 && old.CycleDay == 28 && old.FatherName == "Dad" && old.Births == 2 && Near(migrated.CardCycleProgress["card.png"], 27d / 28));
        variableRules.GestationVariation = 0;
        check("Old minimum random sample cannot shorten gestation with zero percent", old.GestationDays(variableRules) == 4);
        foreach (double invalid in new[] { -.01, 1.01, double.NaN })
        {
            var bad = migrated.Clone(); bad.Characters["old"].GestationVariationSample = invalid;
            bool rejected = false; try { bad.Validate(); } catch (InvalidDataException) { rejected = true; }
            check("Invalid normalized variation rejected " + invalid, rejected);
        }

        string directory = Path.Combine(AppContext.BaseDirectory, "resolved-save-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        // Replicate the actual bug: game received 003 (or a_000) but selected a_000.
        foreach (string requestedName in new[] { "003", "a_000" })
        {
            string actual = Path.Combine(directory, "a_000.sav");
            var capture = new PregnancySaveCapture(requestedName, true, migrated);
            double atRequest = old.PregnancyProgress;
            old.PregnancyProgress += .1;
            check("Native auto capture waits for actual destination " + requestedName, capture.Request == null && capture.Matches(requestedName, true) && !capture.Matches(requestedName, false));
            capture.Resolve(actual);
            File.WriteAllText(actual, "Native autosave " + requestedName);
            capture.Request.Commit(PregnancyStorage.Hash(actual), "");
            var restored = PregnancyStorage.Load(actual, "", out _);
            check("Native auto state follows resolved slot and frozen snapshot " + requestedName, Near(restored.Characters["old"].PregnancyProgress, atRequest) && !File.Exists(Path.Combine(directory, "a_003.sav.alpregnancy.json")) && !File.Exists(Path.Combine(directory, "a_a_000.sav.alpregnancy.json")));
            bool rejected = false; try { capture.Resolve(Path.Combine(directory, "other.sav")); } catch (InvalidOperationException) { rejected = true; }
            check("Resolved save cannot be rebound to another slot " + requestedName, rejected && !capture.Matches(requestedName, true));
        }
        string before = PregnancyStorage.Hash(PregnancyStorage.Sidecar(Path.Combine(directory, "a_000.sav")));
        var cancelled = new PregnancySaveCapture("004", false, migrated);
        PregnancySimulation.NextDay(migrated, variableRules, random);
        check("Unsaved day and cancelled native save leave autosave unchanged", cancelled.Request == null && PregnancyStorage.Hash(PregnancyStorage.Sidecar(Path.Combine(directory, "a_000.sav"))) == before && !File.Exists(Path.Combine(directory, "004.sav.alpregnancy.json")));
    }
}
