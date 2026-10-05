using ALPregnancy;

internal static class PregnancyDebugRegression
{
    internal static void Run(Action<string, bool> check)
    {
        var world = new PregnancyWorld { ElapsedDays = 13 };
        var p = world.Register("selected", "Same Name", 17);
        var other = world.Register("other", "Same Name", 2);
        p.RecoveryProgress = .5; p.Residual = .5; p.ResidualFatherKey = "old"; p.ResidualFatherName = "Old";
        p.Births = 2; p.LastFatherName = "History"; p.LastBirthDay = 3;
        var rules = new PregnancyRules { Mode = ConceptionMode.FixedChance, FixedChance = 0, GestationDays = 280 };
        bool Edit(PregnancyDebugCommand command, double value = 0) => PregnancyDebugActions.Apply(world, world, p.Key, command, value, rules);
        check("Debug force overrides zero probability and recovery", Edit(PregnancyDebugCommand.ForceConceive) && p.IsPregnant && p.PregnancyProgress == 0 && p.RecoveryProgress == 1);
        check("Manual gestation uses configured duration without a random reroll", p.GestationDays(rules) == 280);
        check("Manual conception clears residual and marks debug parent", p.Residual == 0 && p.ResidualFatherKey == "" && p.ResidualFatherName == "" && p.FatherKey == "debug");
        check("Debug selection is by stable key even when names match", !other.IsPregnant && other.CycleDay == 2);
        check("Debug edits preserve calendar and birth history", p.CycleDay == 17 && world.ElapsedDays == 13 && p.Births == 2 && p.LastBirthDay == 3 && p.LastFatherName == "History");
        check("Day input can set actual pregnancy progress", Edit(PregnancyDebugCommand.SetDay, 160) && p.PregnancyProgress == 160d / 280 && Math.Abs(p.Growth(40) - .5f) < 1e-6);
        string father = p.FatherName;
        check("Already pregnant force command leaves existing progress alone", !Edit(PregnancyDebugCommand.ForceConceive) && p.PregnancyProgress == 160d / 280 && p.FatherName == father);
        check("Negative and out-of-range day input are rejected", !Edit(PregnancyDebugCommand.SetDay, -1) && !Edit(PregnancyDebugCommand.SetDay, 281) && p.PregnancyProgress == 160d / 280);
        check("Full-term preset shows full belly without advancing the world", Edit(PregnancyDebugCommand.SetDay, 280) && p.Growth(40) == 1 && p.IsPregnant && p.Births == 2 && world.ElapsedDays == 13);
        check("Setting back to day zero restores zero belly growth", Edit(PregnancyDebugCommand.SetDay, 0) && p.Growth(40) == 0 && p.IsPregnant);
        var stale = world.Clone();
        check("Editor from a previous load cannot modify same-id current world", !PregnancyDebugActions.Apply(world, stale, p.Key, PregnancyDebugCommand.SetDay, 200, rules) && p.PregnancyProgress == 0);
        check("Unknown selected key cannot mutate another character", !PregnancyDebugActions.Apply(world, world, "missing", PregnancyDebugCommand.ForceConceive, 0, rules) && !other.IsPregnant);
        check("Unavailable game world rejects editing", !PregnancyDebugActions.Apply(null, world, p.Key, PregnancyDebugCommand.ForceConceive, 0, rules));
        check("Clear pregnancy is not birth and does not alter history", Edit(PregnancyDebugCommand.ClearPregnancy) && !p.IsPregnant && p.RecoveryProgress == 1 && p.Births == 2 && p.LastBirthDay == 3 && p.FatherKey == "" && p.FatherName == "");
        check("A day change cannot implicitly conceive an unpregnant character", !Edit(PregnancyDebugCommand.SetDay, 100) && !p.IsPregnant);
        p.RecoveryProgress = .75;
        check("Reset cooldown only enables conception and retains cycle/history", Edit(PregnancyDebugCommand.ResetCooldown) && p.CanConceive(rules) && p.CycleDay == 17 && p.Births == 2);
        var random = new Random(2);
        Edit(PregnancyDebugCommand.ForceConceive);
        Edit(PregnancyDebugCommand.SetDay, p.GestationDays(rules));
        PregnancySimulation.NextDay(world, rules, random);
        check("Full-term debug state still births on the next actual day", !p.IsPregnant && p.Births == 3 && p.RecoveryProgress == 0 && world.ElapsedDays == 14);

        string directory = Path.Combine(AppContext.BaseDirectory, "debug-save-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string slotA = Path.Combine(directory, "000.sav"), slotB = Path.Combine(directory, "001.sav");
        File.WriteAllText(slotA, "native A");
        new PregnancySaveRequest(slotA, world).Commit(PregnancyStorage.Hash(slotA), "A");
        string hashA = PregnancyStorage.Hash(PregnancyStorage.Sidecar(slotA));
        var filesBefore = Directory.GetFiles(directory).Order().ToArray();
        Edit(PregnancyDebugCommand.ForceConceive);
        Edit(PregnancyDebugCommand.SetDay, 200);
        check("Debug state/progress edits do not create or overwrite save files", Directory.GetFiles(directory).Order().SequenceEqual(filesBefore) && PregnancyStorage.Hash(PregnancyStorage.Sidecar(slotA)) == hashA);
        var request = new PregnancySaveRequest(slotB, world);
        Edit(PregnancyDebugCommand.SetDay, 250);
        File.WriteAllText(slotB, "native B"); request.Commit(PregnancyStorage.Hash(slotB), "B");
        var loadedB = PregnancyStorage.Load(slotB, "B", out _);
        check("Selected slot saves debug state frozen at the save request", loadedB.Characters[p.Key].IsPregnant && loadedB.Characters[p.Key].PregnancyProgress == 200d / 280);
        check("Debug saves do not overwrite the previously loaded slot", PregnancyStorage.Hash(PregnancyStorage.Sidecar(slotA)) == hashA);
        var loadedA = PregnancyStorage.Load(slotA, "A", out _);
        check("Reload restores pre-debug pregnancy and recovery from that slot", !loadedA.Characters[p.Key].IsPregnant && loadedA.Characters[p.Key].RecoveryProgress == 0);
        world.Validate(); loadedB.Validate();
    }
}

