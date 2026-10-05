namespace ALPregnancy;

// Explicit anatomical correspondences seen in the user's AL clothing/body trace.
// Never strip arbitrary prefixes: helpers and animation joints are not skin bones.
internal static class RigBoneNames
{
    internal static string Canonical(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return name switch
        {
            "cf_c_spine_waist01" => "cf_s_waist01",
            "cf_c_spine_waist02" => "cf_s_waist02",
            "cf_c_spine_01" => "cf_s_spine01",
            "cf_c_spine_02" => "cf_s_spine02",
            "cf_c_spine_03" => "cf_s_spine03",
            "cf_c_spine_kokan" => "cf_s_kokanskin",
            "cf_c_spine_siri_L" => "cf_s_siri_L",
            "cf_c_spine_siri_R" => "cf_s_siri_R",
            "cf_c_spine_siri_C" => "cf_s_siri_C",
            "cf_c_spine_hipleg1_L" => "cf_s_hipleg1_L",
            "cf_c_spine_hipleg1_R" => "cf_s_hipleg1_R",
            "cf_c_spine_hipleg2_L" => "cf_s_hipleg2_L",
            "cf_c_spine_hipleg2_R" => "cf_s_hipleg2_R",
            "cf_c_leg_upper1_L" => "cf_s_thigh01_L",
            "cf_c_leg_upper1_R" => "cf_s_thigh01_R",
            "cf_c_leg_upper2_L" => "cf_s_thigh02_L",
            "cf_c_leg_upper2_R" => "cf_s_thigh02_R",
            "cf_c_leg_upper3_L" => "cf_s_thigh03_L",
            "cf_c_leg_upper3_R" => "cf_s_thigh03_R",
            _ => name
        };
    }
}
