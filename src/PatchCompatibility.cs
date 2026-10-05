using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace ALPregnancy;

internal static class PatchCompatibility
{
    internal const string UncensorGuid = "com.illgames.al.uncensor";
    internal static string Status { get; private set; } = "Uncensor: checking...";

    internal static void Refresh(bool forceLog = false)
    {
        bool loaded = IL2CPPChainloader.Instance.Plugins.TryGetValue(UncensorGuid, out var plugin) && plugin.Instance != null;
        bool installed = File.Exists(Path.Combine(Paths.PluginPath, "AL_Uncensor", "AL_Uncensor.dll"));
        string next = loaded ? "Uncensor: loaded" : installed
            ? "Uncensor: failed to load; belly uses available compatible body."
            : "Uncensor: not detected";
        // Uncensor is optional. Its startup status does not describe the actual
        // body's readability or skeleton. MorphReadiness and BodyMeshSelection
        // validate those at native completion for both gameplay and preview.
        if (next != Status || forceLog)
        {
            if (loaded || !installed) PregnancyPlugin.Logger.LogInfo(next);
            else PregnancyPlugin.Logger.LogWarning(next + " Check its startup error in LogOutput.log; restart to restore Uncensor's own features. Pregnancy deformation is not disabled by this optional plugin.");
        }
        Status = next;
    }
}
