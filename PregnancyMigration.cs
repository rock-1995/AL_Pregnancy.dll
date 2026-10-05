using System.Text.Json;
using System.Text.Json.Nodes;

namespace ALPregnancy;

public static class PregnancyMigration
{
    // Migration is memory-only; the next actual game save writes v3 alongside that slot.
    public static PregnancyWorld Read(JsonElement json, PregnancyRules rules, int startDay = 40)
    {
        int version = json.GetProperty("Version").GetInt32();
        if (version == 3)
        {
            var current = JsonSerializer.Deserialize<PregnancyWorld>(json.GetRawText());
            current.Validate();
            return current;
        }
        if (version != 1 && version != 2) throw new InvalidDataException("Unsupported pregnancy save version.");
        var node = JsonNode.Parse(json.GetRawText());
        foreach (var entry in node["Characters"].AsObject())
        {
            var p = entry.Value.AsObject();
            int cycle = p["CycleDay"].GetValue<int>();
            if (cycle < 1 || cycle > 28) throw new InvalidDataException("Invalid legacy cycle: " + entry.Key);
            p["CycleProgress"] = CyclePhase.Progress(cycle);
            p.Remove("CycleDay");
            if (version == 2)
            {
                double roll = p["GestationVariationRoll"]?.GetValue<double>() ?? 0;
                if (!double.IsFinite(roll) || Math.Abs(roll) > 1) throw new InvalidDataException("Invalid legacy variation: " + entry.Key);
                p["GestationVariationSample"] = (roll + 1) / 2;
                p.Remove("GestationVariationRoll");
                continue;
            }
            int day = p["PregnantDays"].GetValue<int>(), due = p["DueDays"].GetValue<int>(), remaining = p["RecoveryDays"].GetValue<int>();
            if (day < -1 || due < 1 || due > 10000 || remaining < 0)
                throw new InvalidDataException("Invalid legacy progress: " + entry.Key);
            bool pregnant = day >= 0;
            p["Pregnant"] = pregnant;
            p["PregnancyProgress"] = pregnant ? Math.Clamp(day / (double)due, 0, 1) : 0;
            // v1 did not store the initial recovery length. Use the current setting, or
            // the saved remaining days if greater, to avoid losing an active recovery.
            p["RecoveryProgress"] = pregnant || remaining == 0 ? 1 :
                1 - remaining / (double)Math.Max(remaining, Math.Max(1, rules.RecoveryDays));
            p["GestationVariationSample"] = .5;
            // Preserve the previous growth curve where it was usable. Short test pregnancies
            // that never reached StartDay use the standard normalized curve instead.
            p["GrowthStartScale"] = pregnant && due > startDay ? 280d / due : 1;
            foreach (var old in new[] { "PregnantDays", "DueDays", "RecoveryDays", "IsPregnant", "CanConceive" }) p.Remove(old);
        }
        var cards = new JsonObject();
        foreach (var entry in node["CardCycles"].AsObject())
        {
            int day = entry.Value.GetValue<int>();
            if (day < 1 || day > 28) throw new InvalidDataException("Invalid legacy card cycle.");
            cards[entry.Key] = CyclePhase.Progress(day);
        }
        node["CardCycleProgress"] = cards;
        node.AsObject().Remove("CardCycles");
        node["Version"] = 3;
        var world = JsonSerializer.Deserialize<PregnancyWorld>(node.ToJsonString());
        world.Validate();
        return world;
    }
}
