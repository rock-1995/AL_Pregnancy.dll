namespace ALPregnancy;

// Pure ranking policy, also exercised by the offline regression harness.
internal static class TorsoSelectionPolicy
{
    internal static long Score(string meshName, bool readable, bool owned, bool bindFrame,
        int abdomenVertices, bool active)
    {
        if (!readable || !owned || !bindFrame || abdomenVertices < 16 ||
            (meshName ?? "").Contains("armleg", StringComparison.OrdinalIgnoreCase)) return -1;
        // Visibility is a tier, not a small weight: a large hidden alternative
        // must never outrank a valid body piece currently shown on screen.
        return (active ? 1L << 32 : 0) + abdomenVertices;
    }

    internal static bool IsBodyPiece(bool registered, string rendererName, string meshName)
    {
        if (registered) return true;
        // AL's upper/lower/onepi variants must come from BodyRenderers. Their
        // names alone can also describe clothing, so do not guess those roles.
        return (rendererName ?? "").StartsWith("o_body", StringComparison.OrdinalIgnoreCase) ||
               (meshName ?? "").StartsWith("o_body", StringComparison.OrdinalIgnoreCase);
    }
}
