using ALPregnancy;
using System.Text.Json;

internal static class PregnancyRegression
{
    internal static void Run(Action<string, bool> check)
    {
        var random = new Random(20261004);
        var rules = new PregnancyRules { Mode = ConceptionMode.FixedChance, FixedChance = 0, GestationDays = 280 };
        var world = new PregnancyWorld();
        var p = world.Register("a", "Female A", 28);
        check("Character cycle is assigned once, not rerolled by scanning", ReferenceEquals(p, world.Register("a", "Female A", 9)) && p.CycleDay == 28);
        check("0 percent never conceives", !Enumerable.Range(0, 1000).Any(_ => PregnancySimulation.Exposure(p, rules, "dad", "Father", random)) && !p.IsPregnant);
        rules.FixedChance = 1;
        check("Fixed 100 percent ignores safe day", PregnancySimulation.Exposure(p, rules, "dad", "Father", random) && p.IsPregnant && p.PregnancyProgress == 0);
        check("Configured duration and father retained", p.GestationDays(rules) == 280 && p.FatherName == "Father" && p.FatherKey == "dad");
        check("Cannot conceive again while pregnant", !PregnancySimulation.Exposure(p, rules, "other", "Other", random) && p.FatherKey == "dad");
        check("Growth before start day is zero", p.Growth(40) == 0);
        p.PregnancyProgress = 40d / 280;
        check("Growth starts at configured day", p.Growth(40) == 0);
        p.GestationVariationSample = .5; p.PregnancyProgress = 160d / 280;
        check("SVS smoothstep growth at half interval", Math.Abs(p.Growth(40) - .5f) < 1e-6);
        p.PregnancyProgress = 279d / 280; world.CardCycleProgress["card.png"] = 27d / 28;
        PregnancySimulation.NextDay(world, rules, random);
        check("Birth occurs once at due day", !p.IsPregnant && p.Births == 1 && p.LastFatherName == "Father" && p.LastBirthDay == 1);
        check("SVS postpartum cooldown defaults to 180", p.RecoveryProgress == 0 && !PregnancySimulation.Exposure(p, rules, "dad", "Father", random));
        check("Cycle wraps 28 to 1, including directory-only cards", p.CycleDay == 1 && world.CardCycleProgress["card.png"] == 0);
        rules.RecoveryDays = 1;
        PregnancySimulation.NextDay(world, rules, random);
        check("One-day recovery completes; cycle advances only once", p.RecoveryProgress == 1 && p.CycleDay == 2 && p.Births == 1);
        rules.Mode = ConceptionMode.SimpleCycle;
        check("Simple mode has safe normal and danger bands", rules.EventChance(1) == 0 && rules.EventChance(8) == .05 && rules.EventChance(14) == .5 && rules.EventChance(20) == 0);
        rules.SafeChance = 1; p.CycleDay = 1;
        check("Safe chance is configurable up to 100 percent", PregnancySimulation.Exposure(p, rules, "x", "X", random));
        check("Ranges may wrap over day 28", PregnancyRules.Within(28, 26, 3) && PregnancyRules.Within(1, 26, 3) && !PregnancyRules.Within(10, 26, 3));
        var complex = new PregnancyWorld();
        var q = complex.Register("b", "Female B", 12);
        rules = new PregnancyRules { Mode = ConceptionMode.ComplexCycle, ComplexChance = 1, ResidualDecay = 1 };
        check("COM style event stores residual instead of immediate roll", !PregnancySimulation.Exposure(q, rules, "one", "First", random) && q.Residual == 1 && !q.IsPregnant);
        PregnancySimulation.Exposure(q, rules, "two", "Second", random);
        check("COM residual resets to one and records latest father", q.Residual == 1 && q.ResidualFatherKey == "two");
        PregnancySimulation.NextDay(complex, rules, random);
        check("Complex non-ovulation day has no conception", !q.IsPregnant && q.CycleDay == 13);
        PregnancySimulation.NextDay(complex, rules, random);
        check("Complex evaluates outgoing day before calendar increment", !q.IsPregnant && q.CycleDay == 14);
        PregnancySimulation.NextDay(complex, rules, random);
        check("Complex guaranteed conception on ovulation with full residual", q.IsPregnant && q.FatherName == "Second" && q.Residual == 0 && q.PregnancyProgress == 1d / 280);
        check("COM egg coefficients are 1 and 0.5", rules.Egg(14) == 1 && rules.Egg(15) == .5 && rules.Egg(16) == 0);
        rules.OvulationDay = 28;
        check("Ovulation next day wraps to 1", rules.Egg(28) == 1 && rules.Egg(1) == .5);
        var residualWorld = new PregnancyWorld(); var r = residualWorld.Register("c", "C", 1);
        rules = new PregnancyRules { Mode = ConceptionMode.ComplexCycle, ComplexChance = 0 };
        PregnancySimulation.Exposure(r, rules, "dad", "Father", random);
        PregnancySimulation.NextDay(residualWorld, rules, random);
        check("28-day residual decays by 0.75", Math.Abs(r.Residual - .75) < 1e-9);
        for (int i = 0; i < 8; i++) PregnancySimulation.NextDay(residualWorld, rules, random);
        check("Residual below 0.1 expires with father attribution", r.Residual == 0 && r.ResidualFatherKey == "");
        rules.Mode = ConceptionMode.FixedChance;
        int originalCycle = r.CycleDay;
        check("Switching mode does not reroll cycle or reset pregnancy", r.CycleDay == originalCycle && q.IsPregnant);
        var boundary = new DayBoundary(); boundary.Reset(0);
        check("Initial morning and repeated observations do not advance", !boundary.Observe(0) && !boundary.Observe(1) && !boundary.Observe(2));
        check("Evening to night is not next day", !boundary.Observe(3) && !boundary.Observe(4));
        check("Night event to morning advances once", boundary.Observe(0) && !boundary.Observe(0));
        boundary.Reset(4); boundary.Reset(0);
        check("Loading another slot at morning is not a day transition", !boundary.Observe(0));
        var clone = complex.Clone();
        q.PregnancyProgress = 190d / 280;
        check("Save snapshot is immutable after live state changes", clone.Characters["b"].PregnancyProgress == 1d / 280);
        clone.Validate();
        clone.Characters["b"].CycleDay = 29;
        bool rejected = false; try { clone.Validate(); } catch (InvalidDataException) { rejected = true; }
        check("Invalid cycle in save rejected", rejected);

        string directory = Path.Combine(AppContext.BaseDirectory, "pregnancy-save-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string slot = Path.Combine(directory, "000.sav");
        File.WriteAllText(slot, "Native save A");
        string hashA = PregnancyStorage.Hash(slot);
        PregnancyStorage.Commit(slot, hashA, "time A", complex.Clone());
        var loaded = PregnancyStorage.Load(slot, "time A", out _);
        check("Pregnancy save round trips all state", loaded.Characters["b"].PregnancyProgress == 190d / 280 && loaded.Characters["b"].FatherName == "Second" && loaded.WorldId == complex.WorldId);
        loaded.Characters["b"].PregnancyProgress = 200d / 280;
        File.WriteAllText(slot, "Native save B");
        string hashB = PregnancyStorage.Hash(slot);
        PregnancyStorage.Commit(slot, hashB, "time B", loaded);
        File.WriteAllText(slot, "Native save A");
        loaded = PregnancyStorage.Load(slot, "time A", out string status);
        check("Native rollback selects matching companion backup", loaded.Characters["b"].PregnancyProgress == 190d / 280 && status.Contains("backup"));
        File.WriteAllText(slot, "Unrelated native save");
        rejected = false; try { PregnancyStorage.Load(slot, "time C", out _); } catch (InvalidDataException) { rejected = true; }
        check("Mismatched slot never imports another timeline", rejected);
        string slotTwo = Path.Combine(directory, "001.sav"); File.WriteAllText(slotTwo, "Second slot");
        var second = PregnancyStorage.Load(slotTwo, "time D", out _);
        check("Pre-plugin save initializes independently", second.Characters.Count == 0 && second.WorldId != complex.WorldId);
        check("Reading a pre-plugin slot does not create an external save", !File.Exists(PregnancyStorage.Sidecar(slotTwo)));
        var beforeFiles = Directory.GetFiles(directory).Order().ToArray();
        int initialCycle = second.EnsureCardCycle("female/test.png", random);
        check("First card scan initializes 1-28 in the current world", initialCycle is >= 1 and <= 28);
        check("Repeated scan keeps the current in-memory cycle", second.EnsureCardCycle("female/test.png", random) == initialCycle);
        var memoryOnly = second.Register("unsaved", "Unsaved", initialCycle);
        var always = new PregnancyRules { Mode = ConceptionMode.FixedChance, FixedChance = 1 };
        PregnancySimulation.Exposure(memoryOnly, always, "dad", "Father", random);
        PregnancySimulation.NextDay(second, always, random);
        check("Scanning, conception and daily updates do not create files", Directory.GetFiles(directory).Order().SequenceEqual(beforeFiles));
        var discarded = PregnancyStorage.Load(slotTwo, "time D", out _);
        check("Reload discards unsaved pregnancy and unsaved card cycles", discarded.Characters.Count == 0 && discarded.CardCycleProgress.Count == 0);
        check("Loading is read-only", Directory.GetFiles(directory).Order().SequenceEqual(beforeFiles));
        string beforeSlotA = PregnancyStorage.Hash(PregnancyStorage.Sidecar(slot));
        loaded.Characters["b"].PregnancyProgress = 201d / 280;
        loaded.EnsureCardCycle("saved-card.png", random);
        var request = new PregnancySaveRequest(slotTwo, loaded);
        loaded.Characters["b"].PregnancyProgress = 230d / 280;
        check("Save capture remains in memory until native save completes", Directory.GetFiles(directory).Order().SequenceEqual(beforeFiles));
        File.WriteAllText(slotTwo, "Completed native save in selected slot B");
        request.Commit(PregnancyStorage.Hash(slotTwo), "time E");
        var savedB = PregnancyStorage.Load(slotTwo, "time E", out _);
        check("Save after loading A writes the explicitly selected B slot", savedB.Characters["b"].PregnancyProgress == 201d / 280 && PregnancyStorage.Hash(PregnancyStorage.Sidecar(slot)) == beforeSlotA);
        check("Saved card cycle is restored with its selected slot", savedB.CardCycleProgress["saved-card.png"] == loaded.CardCycleProgress["saved-card.png"]);
        File.WriteAllText(slot, "Native save A");
        var reloadedA = PregnancyStorage.Load(slot, "time A", out _);
        check("Reloading A does not inherit B or later unsaved progress", reloadedA.Characters["b"].PregnancyProgress == 190d / 280);
        string cancelledSlot = Path.Combine(directory, "cancelled.sav");
        var cancelled = new PregnancySaveRequest(cancelledSlot, loaded);
        check("Failed or cancelled save leaves no pending or external file", !File.Exists(PregnancyStorage.Sidecar(cancelledSlot)) && !File.Exists(cancelledSlot + ".alpregnancy.pending.json"));
        check("No global cycle registry is created", !File.Exists(Path.Combine(directory, "ALPregnancy_initial_cycles.json")));
        File.WriteAllText(PregnancyStorage.Sidecar(slotTwo), "{ broken");
        rejected = false; try { PregnancyStorage.Load(slotTwo, "new", out _); } catch (InvalidDataException) { rejected = true; }
        check("Damaged companion fails closed without erasing data", rejected && File.ReadAllText(PregnancyStorage.Sidecar(slotTwo)) == "{ broken");
        string slotThree = Path.Combine(directory, "002.sav"); File.WriteAllText(slotThree, "Third slot");
        PregnancyStorage.Commit(slotThree, PregnancyStorage.Hash(slotThree), "third", residualWorld);
        using (var writer = new FileStream(slotThree, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            rejected = false; try { PregnancyStorage.Hash(slotThree); } catch (IOException) { rejected = true; }
            check("Save hashing refuses an active native writer", rejected);
        }
        // Fixtures retained under test output for inspection; no recursive cleanup outside workspace.
    }
}

