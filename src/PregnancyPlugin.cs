using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Character;
using HarmonyLib;
using UnityEngine;

namespace ALPregnancy;

// Preserve the legacy ID so existing configuration and plugin references remain valid.
[BepInPlugin("local.al.pregnancy.preview", "AL Pregnancy", "0.2.27")]
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
        PregnancyRuntime.Install(_harmony);
        MorphReadiness.Install(_harmony);
        BellyVertexMorph.InstallCollisionSync(_harmony);
        Log.LogInfo("AL Pregnancy 0.2.27 loaded. Direct low-poly collision inputs; batch Obi mesh invalidation before world readers to preserve global mesh indices; accepted F8 defaults; original F1 defaults; configurable linear growth stages; independent daily progress variation.");
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
            if (__instance == null || __instance.Disposed || !__instance.HiPoly || __instance.Sex != 1) return;
            if (!MorphReadiness.EnterNativeBoundary(__instance)) return;
            try
            {
                PregnancyRuntime.UpdateHumanVisual(__instance);
                PregnancyController.AfterNativeUpdate(__instance);
                // Even during scene fades/relocation, the existing palette and
                // world bounds must follow this completed native actor pose.
                BellyVertexMorph.UpdateVirtual(__instance);
                BellyVertexMorph.UpdateBodyCollision(__instance);
            }
            finally { MorphReadiness.ExitNativeBoundary(); }
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






