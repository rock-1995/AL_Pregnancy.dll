namespace ALPregnancy;

// AL's additional clothing slots are skinned garments, not freely placed
// o_acs accessories. Include their waist chains/belts in the ordinary cloth
// pipeline; rest-space mapping and the belly footprint still limit deformation.
internal static class SupplementalClothing
{
    internal static bool Matches(string id)
    {
        id = (id ?? "").ToLowerInvariant();
        return id.Contains("add_etc") || id.Contains("addetc") || id.Contains("add_other") ||
               id.Contains("add_arm") || id.Contains("addarm") ||
               id.Contains("add_leg") || id.Contains("addleg");
    }
}
