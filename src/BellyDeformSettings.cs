using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ALPregnancy
{
    // ── JSON data container (internal) ──────────────────────────────────
    internal class BellySettingsData
    {
        /// <summary>
        /// Config format version.  0 (absent) = old absolute-metre radii.
        /// 2 = normalised-by-boneLen radii, old clothOffset-as-distance.
        /// 3 = normalised radii, one global clothOffset-as-displacement-multiplier.
        /// 4 = current normalised radii, per-clothing displacement multipliers.
        /// 5 = adds clothing edge-distortion repair settings.
        /// If version &lt; 2 the Vtx block is ignored and defaults are used instead.
        /// </summary>
        [JsonPropertyName("version")]  public int         Version  { get; set; } = 0;
        [JsonPropertyName("startDay")] public int         StartDay { get; set; } = 40;
        [JsonPropertyName("vtx")]      public VtxSettings Vtx      { get; set; } = new();
    }

    // ── Static settings store ────────────────────────────────────────────
    public static class BellyDeformSettings
    {
        private static readonly string FilePath = Path.Combine(
            BepInEx.Paths.ConfigPath, "ALPregnancy_belly.json");

        public static int         StartDay { get; private set; } = 40;
        public static VtxSettings Vtx      { get; private set; } = new VtxSettings();

        // ── Reset ─────────────────────────────────────────────────────────
        public static void ResetToDefaults()
        {
            StartDay = 40;
            Vtx      = new VtxSettings();
        }

        // ── Delete on-disk config (e.g. to remove an old-format file) ────
        public static void DeleteConfigFile()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception e)
            {
                BepInEx.Logging.Logger.CreateLogSource("ALPregnancy.Belly")
                    .LogWarning("[ALPregnancy] DeleteConfigFile failed: " + e.Message);
            }
        }

        // ── Live preview (in-memory update, no file write) ───────────────
        /// <summary>Apply settings in-memory without touching the JSON file (for live UI preview).</summary>
        public static void SetLive(int startDay, VtxSettings vtx)
        {
            StartDay = startDay;
            Vtx      = vtx;
        }

        // ── Save ─────────────────────────────────────────────────────────
        public static void Save(int startDay, VtxSettings vtx)
        {
            StartDay = startDay;
            Vtx      = vtx;
            try
            {
                var data = new BellySettingsData { Version = 5, StartDay = StartDay, Vtx = Vtx };
                File.WriteAllText(FilePath,
                    JsonSerializer.Serialize(data,
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e)
            {
                BepInEx.Logging.Logger.CreateLogSource("ALPregnancy.Belly")
                    .LogError("[ALPregnancy] BellyDeformSettings.Save failed: " + e.Message);
            }
        }

        // ── Load ─────────────────────────────────────────────────────────
        public static void Load()
        {
            if (!File.Exists(FilePath)) return;
            try
            {
                var log  = BepInEx.Logging.Logger.CreateLogSource("ALPregnancy.Belly");
                var data = JsonSerializer.Deserialize<BellySettingsData>(
                               File.ReadAllText(FilePath));
                if (data == null) return;

                StartDay = data.StartDay;

                if (data.Version < 2)
                {
                    // Old config used absolute metre radii (e.g. radiusSide ≈ 0.18 m).
                    // The current code multiplies every radius by boneLen (≈ 0.15 m), so
                    // the old values would produce a nearly invisible belly (~2.7 cm radius).
                    // Safe fix: discard the old Vtx block and keep defaults.
                    log.LogWarning(
                        "[ALPregnancy] Old config format (v<2, absolute radii). " +
                        "Vtx settings reset to defaults. Re-save from the UI to persist.");
                }
                else if (data.Vtx != null)
                {
                    Vtx = data.Vtx;
                    if (data.Version < 3)
                    {
                        Vtx.ClothOffset = 1.01f;
                        log.LogWarning(
                            "[ALPregnancy] Config v<3 used old clothOffset distance semantics. " +
                            "Only clothOffset was reset to 1.01; other Vtx settings were kept.");
                    }
                    if (data.Version < 4)
                    {
                        float legacy = Vtx.ClothOffset > 0.01f ? Vtx.ClothOffset : 1.01f;
                        Vtx.ClothTopMult = legacy;
                        Vtx.ClothBotMult = legacy;
                        Vtx.ClothBraMult = legacy;
                        Vtx.ClothShortsMult = legacy;
                        Vtx.ClothPanstMult = legacy;
                        Vtx.ClothOtherMult = legacy;
                        log.LogWarning(
                            "[ALPregnancy] Config v<4 used one global cloth multiplier. " +
                            "Copied it to each clothing class multiplier.");
                    }
                }
            }
            catch (Exception e)
            {
                BepInEx.Logging.Logger.CreateLogSource("ALPregnancy.Belly")
                    .LogError("[ALPregnancy] BellyDeformSettings.Load failed: " + e.Message);
            }
        }
    }
}

