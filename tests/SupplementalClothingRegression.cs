using ALPregnancy;
internal static class SupplementalClothingRegression
{
    internal static void Run(Action<string,bool> check)
    {
        foreach(var name in new[]{"o_add_etc00_waist00","add_etc01_waist","o_add_etc01_waist01_wais","o_cf_add_empress/co_addetc_002_00","p_cf_add_other","o_cf_add_arm01","o_cf_add_leg00"})
            check("Supplemental clothing selected: "+name,SupplementalClothing.Matches(name));
        foreach(var name in new[]{"o_body","o_acs_waist_bunny","o_hair","o_face","o_top","cf_s_waist01",""})
            check("Supplemental fallback leaves other meshes alone: "+name,!SupplementalClothing.Matches(name));
        check("Supplemental clothing names are case insensitive",SupplementalClothing.Matches("O_ADD_ETC00_WAIST00"));
    }
}
