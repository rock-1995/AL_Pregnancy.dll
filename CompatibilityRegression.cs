using ALPregnancy;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Character;

internal static class CompatibilityRegression
{
    internal static void Run(Action<string, bool> check, Func<bool, Human> character)
    {
        string file = Path.Combine(Paths.PluginPath, "AL_Uncensor", "AL_Uncensor.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.Delete(file);
        var plugins = IL2CPPChainloader.Instance.Plugins;
        var log = PregnancyPlugin.Logger;
        plugins.Clear();
        PatchCompatibility.Refresh();
        check("Absent optional plugin is reported accurately", PatchCompatibility.Status == "Uncensor: not detected");
        var h = character(true);
        h.GameObject.Renderers[0].name = "o_body";
        h.GameObject.Renderers[0].sharedMesh.name = "o_body_base";
        check("Native body accepted without any Uncensor installation", MorphReadiness.EnterNativeBoundary(h));
        MorphReadiness.ExitNativeBoundary();

        File.WriteAllText(file, "fixture only");
        try
        {
            int warnings = log.Warnings.Count;
            PatchCompatibility.Refresh();
            check("Failed optional plugin produces a diagnostic warning", log.Warnings.Count == warnings + 1 && PatchCompatibility.Status.Contains("failed to load"));
            PatchCompatibility.Refresh();
            check("Unchanged optional failure is not logged every scan", log.Warnings.Count == warnings + 1);
            check("Failed optional plugin does not reject completed compatible native body", MorphReadiness.EnterNativeBoundary(h) && MorphReadiness.TryBeginApply(h));
            var source = h.GameObject.Renderers[0].sharedMesh;
            using (var lease = new MeshLease())
            {
                lease.Acquire(h);
                check("Compatible body can acquire private morph mesh after optional failure", lease.ReadableCount == 1 && MeshLease.Owns(h.GameObject.Renderers[0].sharedMesh) && h.GameObject.Renderers[0].sharedMesh != source);
            }
            check("Source protection and restoration still work on fallback body", h.GameObject.Renderers[0].sharedMesh == source);
            MorphReadiness.ExitNativeBoundary();
            check("Optional failure cannot grant apply outside native completion", !MorphReadiness.TryBeginApply(h));

            var renderer = h.GameObject.Renderers[0];
            source.isReadable = false;
            warnings = log.Warnings.Count;
            check("Unreadable native body remains rejected after optional failure", !MorphReadiness.EnterNativeBoundary(h) && !MorphReadiness.TryBeginApply(h));
            check("Rejected unreadable body has a specific diagnostic", log.Warnings.Count == warnings + 1 && log.Warnings[^1].Contains("No readable torso"));
            MorphReadiness.EnterNativeBoundary(h);
            check("Repeated same actor rejection is logged once", log.Warnings.Count == warnings + 1);
            source.isReadable = true;
            var bone = renderer.bones[1];
            renderer.bones[1] = null!;
            check("Missing bone still rejects even with readable source", !MorphReadiness.EnterNativeBoundary(h) && log.Warnings[^1].Contains("Body bone 1 missing"));
            check("Changed rejection reason logs its new cause", log.Warnings.Count == warnings + 2);
            renderer.bones[1] = bone;
            check("Corrected body is admitted immediately and reports recovery", MorphReadiness.EnterNativeBoundary(h) && log.Infos[^1].Contains("now ready"));
            MorphReadiness.ExitNativeBoundary();

            renderer.sharedMesh = null!;
            check("Missing registered body remains rejected", !MorphReadiness.EnterNativeBoundary(h) && log.Warnings[^1].Contains("Registered body mesh missing/empty"));
            warnings = log.Warnings.Count;
            MorphReadiness.Forget(h);
            MorphReadiness.EnterNativeBoundary(h);
            check("Actor disposal forgets its rejection diagnostic", log.Warnings.Count == warnings + 1);
            renderer.sharedMesh = source;

            plugins[PatchCompatibility.UncensorGuid] = new PluginInfo();
            PatchCompatibility.Refresh();
            check("Partial plugin entry without instance is still reported failed", PatchCompatibility.Status.Contains("failed to load"));
            plugins[PatchCompatibility.UncensorGuid].Instance = new object();
            PatchCompatibility.Refresh();
            check("Successful optional plugin is reported loaded", PatchCompatibility.Status == "Uncensor: loaded");
            check("Loaded optional plugin uses the same native completion path", MorphReadiness.EnterNativeBoundary(h));
            MorphReadiness.ExitNativeBoundary();
        }
        finally { File.Delete(file); plugins.Clear(); }
    }
}

namespace BepInEx
{
    internal static class Paths { internal static string PluginPath = Path.Combine(AppContext.BaseDirectory, "optional-plugin-fixture"); }
    internal sealed class PluginInfo { internal object Instance; }
}
namespace BepInEx.Unity.IL2CPP
{
    internal sealed class IL2CPPChainloader
    {
        internal static readonly IL2CPPChainloader Instance = new();
        internal readonly Dictionary<string, BepInEx.PluginInfo> Plugins = new();
    }
}
