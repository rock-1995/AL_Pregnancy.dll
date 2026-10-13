using Character;
using HarmonyLib;
using ILLGAMES.Rigging;

namespace ALPregnancy;

// Patch the callers, not HumanBody.GetShapeBodyValue: native IK uses an inlined
// copy of that getter. Leave the game's contact offsets, masks and covers intact.
internal static class BodyChangeAnimation
{
    // These eight native implementations read BustSize directly through the
    // inlined getter and write the breast blend parameter to both animators.
    internal static readonly Type[] AnimatorReaders = {
        typeof(AL.H.Motion.Caress.Caress00.Controller), typeof(AL.H.Motion.Caress.Caress01.Controller),
        typeof(AL.H.Motion.Service.Service00.Controller), typeof(AL.H.Motion.Service.Service01.Controller),
        typeof(AL.H.Motion.Insert.Insert00.Controller), typeof(AL.H.Motion.Insert.Insert01.Controller),
        typeof(AL.H.Motion.Les00.Controller), typeof(AL.H.Motion.Les01.Controller)
    };
    internal static void Install(Harmony harmony)
    {
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(MotionIK)))
        {
            if (method.Name != "GetCorrectShapeValues" && method.Name != "GetCorrectShapeValuesParam2") continue;
            int bodies = method.GetParameters().Count(p => p.ParameterType == typeof(HumanBody));
            if (bodies != 1 && bodies != 2) throw new InvalidOperationException("Unknown native IK shape signature: " + method);
            harmony.Patch(method,
                prefix: new HarmonyMethod(typeof(BodyChangeAnimation), bodies == 1 ? nameof(BeforeSingle) : nameof(BeforePair)),
                finalizer: new HarmonyMethod(typeof(BodyChangeAnimation), nameof(AfterRead)));
        }
        // Scene motion correction also asks its actor for breast/shape values.
        foreach (string name in new[] { "get_Breast", "GetShape" })
            harmony.Patch(AccessTools.Method(typeof(AL.H.HActor), name),
                prefix: new HarmonyMethod(typeof(BodyChangeAnimation), nameof(BeforeActor)),
                finalizer: new HarmonyMethod(typeof(BodyChangeAnimation), nameof(AfterRead)));
        foreach (var type in AnimatorReaders)
            harmony.Patch(AccessTools.Method(type, "UpdateAnimeParam"),
                prefix: new HarmonyMethod(typeof(BodyChangeAnimation), nameof(BeforeAnimator)),
                finalizer: new HarmonyMethod(typeof(BodyChangeAnimation), nameof(AfterAnimator)));
    }

    internal sealed class AnimatorReadScope : IDisposable
    {
        private readonly List<ReadScope> _scopes = new();
        internal static AnimatorReadScope Begin(AL.H.Motion.Base.Controller controller)
        {
            var actors = controller?._actors;
            AnimatorReadScope result = null;
            try
            {
                if (actors != null) foreach (var actor in actors)
                {
                    var scope = ReadScope.Begin(actor?.Human?.Body, null);
                    if (scope == null) continue;
                    result ??= new AnimatorReadScope();
                    result._scopes.Add(scope);
                }
                return result;
            }
            catch { result?.Dispose(); throw; }
        }
        public void Dispose()
        {
            // Dispose every actor even if a native object was destroyed mid-call.
            Exception failure = null;
            for (int i = _scopes.Count - 1; i >= 0; i--)
                try { _scopes[i].Dispose(); } catch (Exception ex) { failure ??= ex; }
            _scopes.Clear();
            if (failure != null) throw failure;
        }
    }

    // A live F8 progress change can happen after this motion's parameters were
    // initialized. Refresh only the native parameter calculation, once per body
    // change. The next ordinary Animator evaluation consumes the new values.
    internal static bool RefreshAnimatorParameters(Human human)
    {
        var scene = AL.H.HScene.Instance;
        if (scene == null || scene.IsDisposed || !scene.Loaded) return false;
        var controller = scene.Controller;
        var actors = controller?._actors;
        if (actors == null || !actors.Any(a => a?.Human?.Pointer == human?.Pointer)) return false;
        using (AnimatorReadScope.Begin(controller)) controller.UpdateAnimeParam();
        PregnancyPlugin.Logger.LogInfo("[Animation contact] Refreshed native animator parameters after body change.");
        return true;
    }

    internal static void BeforeAnimator(AL.H.Motion.Base.Controller __instance, out AnimatorReadScope __state)
        => __state = AnimatorReadScope.Begin(__instance);
    internal static Exception AfterAnimator(Exception __exception, AnimatorReadScope __state)
    {
        __state?.Dispose();
        return __exception;
    }

    internal sealed class ReadScope : IDisposable
    {
        private readonly Human _first, _second;
        private BodyChangeRuntime.Scope _firstScope, _secondScope;
        private ReadScope(Human first, BodyChangeRuntime.Scope firstScope, Human second, BodyChangeRuntime.Scope secondScope)
        {
            _first = first; _firstScope = firstScope;
            _second = second; _secondScope = secondScope;
        }
        internal static ReadScope Begin(HumanBody first, HumanBody second)
        {
            var firstHuman = first?._human;
            var secondHuman = second?._human;
            BodyChangeRuntime.Scope firstScope = null, secondScope = null;
            try
            {
                firstScope = BodyChangeRuntime.BeginAnimationRead(first);
                secondScope = BodyChangeRuntime.BeginAnimationRead(second);
                return firstScope == null && secondScope == null ? null : new ReadScope(firstHuman, firstScope, secondHuman, secondScope);
            }
            catch
            {
                try { BodyChangeRuntime.End(secondHuman, secondScope); }
                finally { BodyChangeRuntime.End(firstHuman, firstScope); }
                throw;
            }
        }
        public void Dispose()
        {
            try { BodyChangeRuntime.End(_second, _secondScope); }
            finally
            {
                BodyChangeRuntime.End(_first, _firstScope);
                _firstScope = _secondScope = null;
            }
        }
    }

    internal static void BeforeSingle(HumanBody body, out ReadScope __state) => __state = ReadScope.Begin(body, null);
    internal static void BeforePair(HumanBody body1, HumanBody body2, out ReadScope __state) => __state = ReadScope.Begin(body1, body2);
    internal static void BeforeActor(AL.H.HActor __instance, out ReadScope __state) => __state = ReadScope.Begin(__instance?.Human?.Body, null);
    internal static Exception AfterRead(Exception __exception, ReadScope __state)
    {
        __state?.Dispose();
        return __exception;
    }
}
