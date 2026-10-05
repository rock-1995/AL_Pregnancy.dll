namespace ALPregnancy;

public enum PregnancyDebugCommand { ForceConceive, ClearPregnancy, SetDay, ResetCooldown, SetProgress, SetRecoveryProgress }

// Explicit character edits only. No probability rolls, time advancement or disk writes.
public static class PregnancyDebugActions
{
    public static bool Apply(PregnancyWorld current, PregnancyWorld expected, string key,
        PregnancyDebugCommand command, double value, PregnancyRules rules)
    {
        // A stale panel from another load (even the same WorldId) must not edit the new world.
        if (current == null || !ReferenceEquals(current, expected) || key == null ||
            !current.Characters.TryGetValue(key, out var p)) return false;
        switch (command)
        {
            case PregnancyDebugCommand.ForceConceive:
                if (p.IsPregnant) return false;
                p.Pregnant = true;
                p.PregnancyProgress = 0;
                p.GestationVariationSample = .5;
                p.GrowthStartScale = 1;
                p.RecoveryProgress = 1;
                p.FatherKey = "debug";
                p.FatherName = "Debug (manual)";
                ClearResidual(p);
                return true;
            case PregnancyDebugCommand.ClearPregnancy:
                if (!p.IsPregnant && p.RecoveryProgress == 1 && p.Residual == 0 &&
                    string.IsNullOrEmpty(p.FatherKey) && string.IsNullOrEmpty(p.FatherName)) return false;
                p.Pregnant = false;
                p.PregnancyProgress = 0;
                p.RecoveryProgress = 1;
                p.FatherKey = p.FatherName = "";
                ClearResidual(p);
                return true;
            case PregnancyDebugCommand.SetDay:
                if (!double.IsFinite(value) || value < 0 || value > p.GestationDays(rules)) return false;
                return Apply(current, expected, key, PregnancyDebugCommand.SetProgress, value / p.GestationDays(rules), rules);
            case PregnancyDebugCommand.SetProgress:
                if (!p.IsPregnant || !double.IsFinite(value) || value < 0 || value > 1 || p.PregnancyProgress == value) return false;
                p.PregnancyProgress = value;
                return true;
            case PregnancyDebugCommand.SetRecoveryProgress:
                if (p.IsPregnant || !double.IsFinite(value) || value < 0 || value > 1 || p.RecoveryProgress == value) return false;
                p.RecoveryProgress = value;
                return true;
            case PregnancyDebugCommand.ResetCooldown:
                if (p.RecoveryProgress == 1) return false;
                p.RecoveryProgress = 1;
                return true;
            default:
                return false;
        }
    }
    private static void ClearResidual(PregnancyRecord p)
    {
        p.Residual = 0;
        p.ResidualFatherKey = p.ResidualFatherName = "";
    }
}
