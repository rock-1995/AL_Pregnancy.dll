using System.Text.Json;
using System.Text.Json.Serialization;

namespace ALPregnancy;

public enum ConceptionMode { SimpleCycle, ComplexCycle, FixedChance }

// These are game rules, not a medical model. Independent of Unity/IL2CPP for regression tests.
public sealed class PregnancyRules
{
    public ConceptionMode Mode { get; set; } = ConceptionMode.SimpleCycle;
    public double FixedChance { get; set; } = .3;
    public double SafeChance { get; set; } = 0;
    public double NormalChance { get; set; } = .05;
    public double DangerChance { get; set; } = .5;
    public int NormalStart { get; set; } = 8;
    public int NormalEnd { get; set; } = 19;
    public int DangerStart { get; set; } = 12;
    public int DangerEnd { get; set; } = 16;
    public double ComplexChance { get; set; } = .3;
    public int OvulationDay { get; set; } = 14;
    public double ResidualDecay { get; set; } = .75;
    public int GestationDays { get; set; } = 280;
    public double GestationVariation { get; set; }
    public int RecoveryDays { get; set; } = 180;
    public double GrowthStart { get; set; } = 0;
    public double GrowthEarlyEnd { get; set; } = .20;
    public double GrowthMiddleEnd { get; set; } = .45;
    public double GrowthLateStart { get; set; } = .70;

    // Each pregnant day receives an independent multiplier, not a fixed due-date roll.
    public double DailyIncrement(double sample)
    {
        double variation = double.IsFinite(GestationVariation) ? Math.Clamp(GestationVariation, 0, 1) : 0;
        double roll = double.IsFinite(sample) ? Math.Clamp(sample, 0, 1) : .5;
        return (1 + (2 * roll - 1) * variation) / Math.Clamp(GestationDays, 1, 1000);
    }

    public static bool Within(int day, int start, int end) => start <= end ? day >= start && day <= end : day >= start || day <= end;
    public string Phase(int day) => Within(day, DangerStart, DangerEnd) ? "Danger" : Within(day, NormalStart, NormalEnd) ? "Normal" : "Safe";
    public double EventChance(int day) => Math.Clamp(Mode == ConceptionMode.FixedChance ? FixedChance : Phase(day) switch { "Danger" => DangerChance, "Normal" => NormalChance, _ => SafeChance }, 0, 1);
    public double Egg(int day) => day == OvulationDay ? 1 : day == OvulationDay % 28 + 1 ? .5 : 0;
}

// Shared by the simulation, diagnostics and UI: show configured probability separately
// from the effective probability after cycle, residual and eligibility conditions.
public sealed class PregnancyChance
{
    public double BaseChance, ResidualFactor, EggFactor, EffectiveChance;
    public string Reason;
    public static PregnancyChance Evaluate(PregnancyRecord p, PregnancyRules rules, bool enabled = true)
    {
        bool complex = rules.Mode == ConceptionMode.ComplexCycle;
        var result = new PregnancyChance
        {
            BaseChance = Math.Clamp(complex ? rules.ComplexChance : rules.EventChance(p.CycleDay), 0, 1),
            ResidualFactor = complex ? p.Residual : 1,
            EggFactor = complex ? rules.Egg(p.CycleDay) : 1,
            Reason = !enabled ? "Gameplay paused in F1" : p.IsPregnant ? "Already pregnant" :
                !p.CanConceive(rules) ? "Postpartum recovery" : ""
        };
        if (result.Reason.Length == 0)
        {
            result.EffectiveChance = result.BaseChance * result.ResidualFactor * result.EggFactor;
            var reasons = new List<string>();
            if (result.BaseChance == 0) reasons.Add("Configured chance is zero");
            if (complex && result.ResidualFactor == 0) reasons.Add("No residual (requires an event)");
            if (complex && result.EggFactor == 0) reasons.Add("Outside ovulation window");
            result.Reason = reasons.Count == 0 ? (complex ? "Checked at day-end" : "Checked per qualifying event") : string.Join("; ", reasons);
        }
        return result;
    }
}

public sealed class PregnancyRecord
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public double CycleProgress { get; set; }
    [JsonIgnore] public int CycleDay { get => CyclePhase.Day(CycleProgress); set => CycleProgress = CyclePhase.Progress(value); }
    public bool Pregnant { get; set; }
    public double PregnancyProgress { get; set; }
    public double RecoveryProgress { get; set; } = 1;
    // Legacy value retained for save/rollback compatibility; no longer controls growth.
    public double GestationVariationSample { get; set; } = .5;
    // Legacy curve metadata retained for rollback; current runtime uses the F1 schedule.
    public double GrowthStartScale { get; set; } = 1;
    public string FatherKey { get; set; } = "";
    public string FatherName { get; set; } = "";
    public double Residual { get; set; }
    public string ResidualFatherKey { get; set; } = "";
    public string ResidualFatherName { get; set; } = "";
    public int Births { get; set; }
    public string LastFatherName { get; set; } = "";
    public long LastBirthDay { get; set; } = -1;
    [JsonIgnore] public bool IsPregnant => Pregnant;
    public bool CanConceive(PregnancyRules rules) => !IsPregnant && (RecoveryProgress >= 1 || rules.RecoveryDays <= 0);
    public double GestationDays(PregnancyRules rules) => Math.Clamp(rules.GestationDays, 1, 1000);
    public double PregnancyDay(PregnancyRules rules) => PregnancyProgress * GestationDays(rules);
    public int RecoveryRemainingDays(PregnancyRules rules) => (int)Math.Ceiling(Math.Max(0, (1 - RecoveryProgress) * Math.Max(0, rules.RecoveryDays) - 1e-10));
    public float Growth(PregnancyRules rules) => IsPregnant ? PregnancyGrowthSchedule.ShapeStage(PregnancyProgress, rules) : 0;
    // Legacy curve evaluator for older save diagnostics. Not used for live visuals.
    public double GrowthStart(int startDay) => Math.Clamp(startDay / 280d * GrowthStartScale, 0, .999999);
    public float Growth(int startDay)
    {
        if (!IsPregnant) return 0;
        double start = GrowthStart(startDay);
        float t = (float)Math.Clamp((PregnancyProgress - start) / (1 - start), 0, 1);
        return t * t * (3 - 2 * t);
    }
}

public static class CyclePhase
{
    public static double Progress(int day) => (day - 1) / 28d;
    public static int Day(double progress) => (int)Math.Floor(progress * 28 + 1e-10) + 1;
    public static double Next(double progress)
    {
        double next = progress + 1d / 28;
        return next >= 1 - 1e-12 ? 0 : next;
    }
}

public sealed class PregnancyWorld
{
    public int Version { get; set; } = 3;
    public string WorldId { get; set; } = Guid.NewGuid().ToString("N");
    public long ElapsedDays { get; set; }
    public Dictionary<string, PregnancyRecord> Characters { get; set; } = new(StringComparer.Ordinal);
    // Directory cards have a cycle even before they join the game. Each save carries its own calendar.
    public Dictionary<string, double> CardCycleProgress { get; set; } = new(StringComparer.Ordinal);
    public PregnancyWorld Clone() => JsonSerializer.Deserialize<PregnancyWorld>(JsonSerializer.Serialize(this));
    public int EnsureCardCycle(string key, Random random)
    {
        if (!CardCycleProgress.TryGetValue(key, out double progress))
            CardCycleProgress[key] = progress = CyclePhase.Progress(random.Next(1, 29));
        return CyclePhase.Day(progress);
    }
    public PregnancyRecord Register(string key, string name, int initialDay)
    {
        if (!Characters.TryGetValue(key, out var value))
            Characters.Add(key, value = new PregnancyRecord { Key = key, Name = name, CycleDay = Math.Clamp(initialDay, 1, 28) });
        if (!string.IsNullOrWhiteSpace(name)) value.Name = name;
        return value;
    }
    public void Validate()
    {
        if (Version != 3 || string.IsNullOrWhiteSpace(WorldId) || ElapsedDays < 0 || Characters == null || CardCycleProgress == null)
            throw new InvalidDataException("Unsupported or invalid pregnancy world.");
        foreach (var item in Characters)
        {
            var p = item.Value;
            if (p == null || p.Key != item.Key || !Unit(p.CycleProgress) || p.CycleProgress >= 1 ||
                !Unit(p.PregnancyProgress) || !Unit(p.RecoveryProgress) || !Unit(p.Residual) ||
                !Unit(p.GestationVariationSample) ||
                !double.IsFinite(p.GrowthStartScale) || p.GrowthStartScale <= 0 || p.GrowthStartScale > 280 ||
                (!p.Pregnant && p.PregnancyProgress != 0) || (p.Pregnant && p.RecoveryProgress != 1))
                throw new InvalidDataException("Invalid character pregnancy record: " + item.Key);
        }
        if (CardCycleProgress.Any(x => !Unit(x.Value) || x.Value >= 1)) throw new InvalidDataException("Invalid card cycle.");
    }
    private static bool Unit(double value) => double.IsFinite(value) && value >= 0 && value <= 1;
}

public static class PregnancySimulation
{
    public static bool Exposure(PregnancyRecord p, PregnancyRules rules, string fatherKey, string fatherName, Random random)
    {
        if (!p.CanConceive(rules)) return false;
        if (rules.Mode == ConceptionMode.ComplexCycle)
        {
            p.Residual = 1;
            p.ResidualFatherKey = fatherKey;
            p.ResidualFatherName = fatherName;
            return false;
        }
        return TryConceive(p, rules, PregnancyChance.Evaluate(p, rules).EffectiveChance, fatherKey, fatherName, random);
    }
    private static bool TryConceive(PregnancyRecord p, PregnancyRules rules, double chance, string fatherKey, string fatherName, Random random)
    {
        if (!p.CanConceive(rules) || chance <= 0 || random.NextDouble() >= Math.Clamp(chance, 0, 1)) return false;
        p.Pregnant = true;
        p.PregnancyProgress = 0;
        p.RecoveryProgress = 1;
        p.GestationVariationSample = .5;
        p.GrowthStartScale = 1;
        p.FatherKey = fatherKey ?? "";
        p.FatherName = fatherName ?? "";
        ClearResidual(p);
        return true;
    }
    public static void NextDay(PregnancyWorld world, PregnancyRules rules, Random random, Action<string> report = null)
    {
        world.ElapsedDays++;
        foreach (var p in world.Characters.Values)
        {
            // Resolve the outgoing night's conception before advancing into the next
            // morning. Fixed/simple and complex conception now share the same day count.
            if (!p.IsPregnant && rules.Mode == ConceptionMode.ComplexCycle && p.Residual > 0 &&
                TryConceive(p, rules, PregnancyChance.Evaluate(p, rules).EffectiveChance, p.ResidualFatherKey, p.ResidualFatherName, random))
                report?.Invoke(p.Name + ": conception at day-end; father " + p.FatherName + "; gestation " + p.GestationDays(rules) + " days.");
            if (p.IsPregnant)
            {
                double increment = rules.DailyIncrement(rules.GestationVariation > 0 ? random.NextDouble() : .5);
                p.PregnancyProgress = AdvanceBy(p.PregnancyProgress, increment);
                if (p.PregnancyProgress >= 1)
                {
                    p.LastFatherName = p.FatherName;
                    p.LastBirthDay = world.ElapsedDays;
                    p.Births++;
                    p.Pregnant = false;
                    p.PregnancyProgress = 0;
                    p.RecoveryProgress = rules.RecoveryDays <= 0 ? 1 : 0;
                    p.FatherKey = p.FatherName = "";
                    ClearResidual(p);
                    report?.Invoke(p.Name + ": birth; recovery " + p.RecoveryRemainingDays(rules) + " days.");
                }
                else report?.Invoke(p.Name + $": pregnancy {p.PregnancyProgress:P2}; today's increment +{increment:P2}; base {p.GestationDays(rules)} days.");
            }
            else if (p.RecoveryProgress < 1 && rules.RecoveryDays > 0) p.RecoveryProgress = Advance(p.RecoveryProgress, rules.RecoveryDays);
            // Residual always ages, including while another mode is selected; switching cannot freeze it.
            p.Residual *= Math.Clamp(rules.ResidualDecay, 0, 1);
            if (p.Residual < .1) ClearResidual(p);
            if (rules.RecoveryDays <= 0) p.RecoveryProgress = 1;
            p.CycleProgress = CyclePhase.Next(p.CycleProgress);
        }
        foreach (var key in world.CardCycleProgress.Keys.ToArray()) world.CardCycleProgress[key] = CyclePhase.Next(world.CardCycleProgress[key]);
    }
    private static void ClearResidual(PregnancyRecord p)
    {
        p.Residual = 0;
        p.ResidualFatherKey = p.ResidualFatherName = "";
    }
    private static double Advance(double progress, double days)
        => AdvanceBy(progress, days <= 0 ? 1 : 1d / days);
    private static double AdvanceBy(double progress, double increment)
    {
        double next = progress + increment;
        return next >= 1 - 1e-12 ? 1 : Math.Clamp(next, 0, 1);
    }
}

public sealed class DayBoundary
{
    private int _last = -1;
    public void Reset(int timeZone) => _last = timeZone;
    public bool Observe(int timeZone)
    {
        bool crossed = _last >= 3 && timeZone == 0;
        _last = timeZone;
        return crossed;
    }
}
