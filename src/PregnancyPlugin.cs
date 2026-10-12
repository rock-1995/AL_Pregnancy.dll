using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Character;
using HarmonyLib;
using UnityEngine;

namespace ALPregnancy;

// Preserve the legacy ID so existing configuration and plugin references remain valid.
[BepInPlugin("local.al.pregnancy.preview", "AL Pregnancy", "0.2.40")]
[BepInDependency(PatchCompatibility.UncensorGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInProcess("AmanatsuLocation.exe")]
public sealed class PregnancyPlugin : BasePlugin
{
    internal static ManualLogSource Logger;
    internal static ConfigEntry<bool> ConfigLog;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<KeyCode> ToggleKey;
    private Harmony _harmony;
    private PregnancyController _controller;
    private static StartupConfigWrite _startupConfig;

    public override void Load()
    {
        Logger = Log;
        _startupConfig = new StartupConfigWrite(Config, message => Log.LogWarning(message));
        try
        {
            Enabled = Config.Bind("General", "Enabled", true, "Enable pregnancy simulation and visuals. Disabling restores meshes and pauses progression; saved state is retained.");
            ToggleKey = Config.Bind("General", "Panel key", KeyCode.F8, "Open or close character pregnancy debug and manual shape preview. Global gameplay settings remain in F1.");
            ConfigLog = Config.Bind("Diagnostics", "Detailed logging", true, "Log mesh and bone compatibility information.");
            PregnancyConfig.Bind(Config);
            _startupConfig.CompleteBinding();
        }
        catch
        {
            _startupConfig.Dispose();
            _startupConfig = null;
            throw;
        }
        PregnancyConfig.Changed += PregnancyRuntime.OnSettingsChanged;
        BellyDeformSettings.Load();
        _controller = AddComponent<PregnancyController>();
        _harmony = new Harmony("local.al.pregnancy.preview");
        // Release cloned meshes before the game tears down its character object.
        _harmony.Patch(AccessTools.Method(typeof(Human), nameof(Human.Dispose)),
            prefix: new HarmonyMethod(typeof(PregnancyPlugin), nameof(BeforeHumanDispose)));
        _harmony.Patch(AccessTools.Method(typeof(Human), "LateUpdate"),
            postfix: new HarmonyMethod(typeof(PregnancyPlugin), nameof(AfterHumanLateUpdate)) { priority = Priority.Last });
        _harmony.Patch(AccessTools.Method(typeof(HumanBody), nameof(HumanBody.UpdateShapeBody)),
            postfix: new HarmonyMethod(typeof(BodyChangeRuntime), nameof(BodyChangeRuntime.NativeShapeUpdated)) { priority = Priority.Last });
        PregnancyRuntime.Install(_harmony);
        MorphReadiness.Install(_harmony);
        BellyVertexMorph.InstallCollisionSync(_harmony);
        Log.LogInfo("AL Pregnancy 0.2.40 loaded. Belly geometry first, native body customization second, virtual binding and pose last.");
    }

    internal static void TickStartupConfig() => _startupConfig?.Tick(Time.unscaledTime);

    private static void BeforeHumanDispose(Human __instance)
    {
        MorphReadiness.Forget(__instance);
        PregnancyRuntime.ReleaseHuman(__instance);
        PregnancyController.ReleaseHuman(__instance);
    }
    private static void AfterHumanLateUpdate(Human __instance)
    {
        try
        {
            NativeVisualUpdate.Complete(__instance);
        }
        catch (Exception ex)
        {
            PregnancyRuntime.ReleaseHuman(__instance);
            PregnancyController.SuspendHuman(__instance);
            Logger.LogError("[Pregnancy] Native completion update: " + ex);
        }
    }

    public override bool Unload()
    {
        _startupConfig?.Tick(Time.unscaledTime, force: true);
        _startupConfig?.Dispose();
        _startupConfig = null;
        PregnancyConfig.Changed -= PregnancyRuntime.OnSettingsChanged;
        PregnancyRuntime.Shutdown();
        if (_controller != null)
        {
            _controller.StopPreview();
            UnityEngine.Object.Destroy(_controller);
        }
        BellyVertexMorph.FinishCollisionSync();
        _harmony?.UnpatchSelf();
        return true;
    }
}






