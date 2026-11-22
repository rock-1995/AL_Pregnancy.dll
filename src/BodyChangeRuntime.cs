using Character;
using HarmonyLib;
using UnityEngine;

namespace ALPregnancy;

internal static class BodyChangeRuntime
{
    private static readonly Dictionary<IntPtr, Entry> Entries = new();
    private static BodyChangeValues _delta;
    private static string _stamp;
    private static float _nextCardCheck;
    private static string DirectoryPath => Path.GetDirectoryName(typeof(PregnancyPlugin).Assembly.Location);

    private sealed class Entry
    {
        internal Human Human;
        internal IntPtr BodyPointer;
        internal HumanDataBody Working;
        internal BodyChangeValues Last;
        internal BodyChangeValues Original;
        internal BodyChangeValues ReadBuffer, ApplyBuffer;
        internal Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> WorkingShape;
        internal BodyChangeCovers Covers;
        internal Scope Active;
        internal bool NativeShapeDirty;
        internal bool AnimatorParametersDirty;
        internal bool Applied;
        internal IntPtr CustomPointer, SourcePointer;
        internal BodyChangeValues Delta;
        internal double Progress;
        internal int Lower, Upper;
        internal float ColorMaximum;
    }

    // The saved body object and its arrays are never edited. The replacement exists
    // only during a synchronous native update and is removed even if it throws.
    internal sealed class Scope : IDisposable
    {
        private readonly HumanDataCustom _owner;
        private readonly HumanDataBody _original, _working;
        private readonly IDisposable _covers;
        private bool _disposed;
        internal Scope(HumanDataCustom owner, HumanDataBody working, BodyChangeCovers covers, Human human)
        {
            _owner = owner; _original = owner.Body; _working = working;
            _covers = covers?.Begin(BodyChangeCovers.Current(human));
            try { owner.Body = working; }
            catch { _covers?.Dispose(); throw; }
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // A native reload is allowed to replace the entire body during the call.
            try { if (_owner.Body?.Pointer == _working.Pointer) _owner.Body = _original; }
            finally { _covers?.Dispose(); }
        }
    }

    private static BodyChangeValues LoadCard(string name)
    {
        var data = new HumanData(1);
        string path = Path.Combine(DirectoryPath, name + ".png");
        if (!data.LoadCharaFile(path, HumanData.LoadFileInfo.Flags.Custom | HumanData.LoadFileInfo.Flags.Parameter) ||
            data.Parameter?.sex != 1 || data.Custom?.Body == null)
            throw new InvalidDataException("Cannot load female body template: " + path);
        var result = BodyChangeValues.Read(data.Custom.Body);
        result.Validate();
        return result;
    }

    private static void RefreshCards()
    {
        if (Time.unscaledTime < _nextCardCheck) return;
        _nextCardCheck = Time.unscaledTime + 2;
        try
        {
            string Stamp(string name) {
                var f = new FileInfo(Path.Combine(DirectoryPath, name + ".png"));
                return f.Exists ? f.Length + ":" + f.LastWriteTimeUtc.Ticks : "missing";
            }
            string stamp = Stamp("base") + "/" + Stamp("reference");
            if (stamp == _stamp) return;
            _stamp = stamp;
            var delta = BodyChangeValues.Difference(LoadCard("base"), LoadCard("reference"));
            ReleaseAll();
            _delta = delta;
            PregnancyPlugin.Logger.LogInfo($"[Body changes] Loaded reference - base: {delta.Shape.Count(v => v != 0)} body sliders, {delta.Extra.Count(v => v != 0)} extra morph controls, nipple color={delta.Nipple != default(Color)}.");
        }
        catch (Exception ex)
        {
            _delta = null;
            ReleaseAll();
            PregnancyPlugin.Logger.LogWarning("[Body changes] Templates unavailable; existing belly deformation continues: " + ex.Message);
        }
    }

    // Native shape updates can be initiated by the game or another plugin.
    // Our own post-belly commit runs inside Active and must not dirty itself.
    internal static void NativeShapeUpdated(HumanBody __instance)
    {
        var human = __instance?._human;
        if (human != null && Entries.TryGetValue(human.Pointer, out var entry) &&
            entry.BodyPointer == __instance.Pointer && entry.Active == null)
            entry.NativeShapeDirty = true;
    }

    // Do not patch ChangeBustSpring/Gravity/AreolaSize/Softness directly. Their
    // Nullable<float> arguments are eight-byte VALUES in the Win64 ABI, while
    // this interop's Harmony trampoline treats them as pointers. None becomes a
    // null memcpy source before the postfix can run. Observe the parameterless
    // native batch instead; direct maker edits are also caught by SourceMatches.
    internal static void InstallDirtyTracking(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(HumanBody), nameof(HumanBody.LateUpdate), Type.EmptyTypes),
            prefix: new HarmonyMethod(typeof(BodyChangeRuntime), nameof(BeforeNativeBodyLateUpdate)) { priority = Priority.First });
        // Maker invokes inlined native setters directly, without the batch flags.
        // Its int/float actions are safe ABI boundaries and also catch a reset
        // which writes the same source value but replaces our visual override.
        harmony.Patch(AccessTools.Method(typeof(CharacterCreation.UI.View.Body.Softness.SoftnessEdit), "_CreateCategory_b__15_3", new[] { typeof(int), typeof(float) }),
            postfix: new HarmonyMethod(typeof(BodyChangeRuntime), nameof(AfterMakerSoftness)));
        harmony.Patch(AccessTools.Method(typeof(CharacterCreation.UI.View.Body.Breast.BreastEdit), "_CreateCategory_b__8_7", new[] { typeof(int), typeof(float) }),
            postfix: new HarmonyMethod(typeof(BodyChangeRuntime), nameof(AfterMakerBreast)));
        harmony.Patch(AccessTools.Method(typeof(CharacterCreation.UI.View.Body.Breast.Nip.NipEdit), "_CreateCategory_b__8_4", new[] { typeof(int), typeof(float) }),
            postfix: new HarmonyMethod(typeof(BodyChangeRuntime), nameof(AfterMakerNip)));
    }

    private static void AfterMakerSoftness(CharacterCreation.UI.View.Body.Softness.SoftnessEdit __instance) => NativeShapeUpdated(__instance?._humanBody);
    private static void AfterMakerBreast(CharacterCreation.UI.View.Body.Breast.BreastEdit __instance) => NativeShapeUpdated(__instance?._humanBody);
    private static void AfterMakerNip(CharacterCreation.UI.View.Body.Breast.Nip.NipEdit __instance) => NativeShapeUpdated(__instance?._humanBody);

    internal static void BeforeNativeBodyLateUpdate(HumanBody __instance)
    {
        var human = __instance?._human;
        if (human == null || !Entries.TryGetValue(human.Pointer, out var entry) ||
            entry.BodyPointer != __instance.Pointer || entry.Active != null) return;
        // AL's real LateUpdate consumes these pending flags, including its
        // inlined bust/softness refreshes, then clears them. Snapshot before it
        // does so. Empty/stable frames keep the no-copy fast path.
        if (__instance._isUpdateBustSize) { entry.NativeShapeDirty = true; return; }
        var groups = __instance._updateShakeFlags;
        if (groups == null) return;
        for (int i = 0; i < groups.Length; i++)
        {
            var flags = groups[i];
            if (flags == null) continue;
            for (int j = 0; j < flags.Length; j++)
                if (flags[j]) { entry.NativeShapeDirty = true; return; }
        }
    }

    internal static void ApplyAfterBelly(Human human, double progress)
    {
        var scope = BeginAfterBelly(human, progress);
        End(human, scope);
        if (human != null && Entries.TryGetValue(human.Pointer, out var entry) && entry.AnimatorParametersDirty &&
            BodyChangeAnimation.RefreshAnimatorParameters(human)) entry.AnimatorParametersDirty = false;
    }

    // MotionIK reads Human.Data.Custom.Body, including through an inlined native
    // GetShapeBodyValue. Supply the last committed visual body only while native
    // contact correction runs, retaining native cover selection and edition conversion.
    // Never refresh shape/physics or recompute pregnancy inside an IK callback.
    internal static Scope BeginAnimationRead(HumanBody body)
    {
        var human = body?._human;
        if (human == null || !PregnancyPlugin.Enabled.Value || PregnancyController.ManualPreviewActive ||
            human.Disposed || human.IsReloading || !human.HiPoly || human.Sex != 1 ||
            human.GameObject == null || !human.GameObject.activeInHierarchy || human.Body?.Pointer != body.Pointer ||
            !Entries.TryGetValue(human.Pointer, out var entry) || entry.BodyPointer != body.Pointer ||
            !entry.Applied || entry.Last == null || entry.Active != null)
            return null;
        var owner = human.Data?.Custom;
        if (owner?.Body == null || owner.Pointer != entry.CustomPointer || owner.Body.Pointer != entry.SourcePointer) return null;
        var scope = new Scope(owner, entry.Working, entry.Covers, human);
        entry.Active = scope;
        return scope;
    }

    internal static Scope BeginAfterBelly(Human human, double progress)
    {
        if (human == null) return null;
        if (Entries.TryGetValue(human.Pointer, out var running) && running.Active != null) return null;
        if (!PregnancyPlugin.Enabled.Value || PregnancyController.ManualPreviewActive ||
            human.Disposed || human.IsReloading || !human.HiPoly || human.Sex != 1 ||
            human.GameObject == null || !human.GameObject.activeInHierarchy || human.Body?.SibBody == null)
        { Release(human); return null; }
        RefreshCards();
        if (_delta == null || progress == 0) { Release(human); return null; }
        var owner = human.Data?.Custom;
        if (owner?.Body?.shapeValueBody == null) return null;
        Entries.TryGetValue(human.Pointer, out var entry);
        if (entry != null && (entry.BodyPointer != human.Body.Pointer ||
            entry.CustomPointer != owner.Pointer || entry.SourcePointer != owner.Body.Pointer))
        {
            Release(human);
            entry = null;
        }
        int lower = PregnancyConfig.BodyLower.Value, upper = PregnancyConfig.BodyUpper.Value;
        float colorMaximum = PregnancyConfig.NippleColorMaximum.Value;
        var covers = BodyChangeCovers.Prepare(entry?.Covers, BodyChangeCovers.Current(human),
            _delta.Shape, progress, lower, upper);
        // Compare live values without copying them. Native/other-plugin array and
        // field writes can bypass every managed setter, so dirty hooks alone are
        // insufficient. Cover comparison also catches edits made in place.
        if (entry != null && !entry.NativeShapeDirty && entry.Progress == progress &&
            ReferenceEquals(entry.Delta, _delta) && entry.Lower == lower && entry.Upper == upper &&
            entry.ColorMaximum == colorMaximum && ReferenceEquals(covers, entry.Covers) &&
            SourceMatches(owner.Body, entry.Original)) return null;
        var original = BodyChangeValues.Read(owner.Body, entry?.ReadBuffer);
        original.Validate();
        var target = original.Apply(_delta, progress, lower, upper, colorMaximum, entry?.ApplyBuffer);
        bool applies = !target.Same(original) || covers?.HasChanges == true;
        if (!applies && entry?.Applied == true)
        {
            Release(human);
            entry = null;
        }
        if (entry == null)
            Entries[human.Pointer] = entry = new Entry { Human = human, BodyPointer = human.Body.Pointer,
                CustomPointer = owner.Pointer, SourcePointer = owner.Body.Pointer };
        if (entry.Active != null) return null; // Defensive against a reentrant native update.
        bool coversChanged = !ReferenceEquals(covers, entry.Covers);
        entry.Covers = covers;
        bool sourceChanged = !original.Same(entry.Original);
        bool nativeChanged = entry.NativeShapeDirty;
        bool changed = !target.Same(entry.Last) || sourceChanged || nativeChanged || coversChanged;
        bool shapeChanged = entry.Last == null || !target.Shape.SequenceEqual(entry.Last.Shape) || coversChanged;
        var previous = entry.Last ?? original;
        var previousOriginal = entry.Original;
        entry.Delta = _delta; entry.Progress = progress;
        entry.Lower = lower; entry.Upper = upper; entry.ColorMaximum = colorMaximum;
        if (!applies)
        {
            // Remember a fully clamped/zero delta too. It must not cause an
            // allocation and full read/recalculation on every subsequent frame.
            entry.Last = target; entry.Original = original;
            entry.ReadBuffer = previousOriginal;
            entry.ApplyBuffer = ReferenceEquals(previous, original) ? null : previous;
            entry.NativeShapeDirty = false;
            return null;
        }
        entry.Working ??= new HumanDataBody();
        entry.Working.Copy(owner.Body);
        target.Write(entry.Working, ref entry.WorkingShape);
        var scope = new Scope(owner, entry.Working, entry.Covers, human);
        entry.Active = scope;
        // Record the attempted target before any native setter, so a partial native
        // failure restores every potentially touched parameter as well as the data.
        entry.Last = target;
        entry.Applied = true;
        try
        {
            if (changed)
            {
                // Static belly/clothing vertices are ready. Commit only the
                // native shape APIs now; do not run a second LateUpdate/physics
                // pass or discard the mesh we just deformed. Stable frames do
                // not schedule native updates.
                RefreshExtra(human.Body, target, previous, sourceChanged || nativeChanged ? original : null);
                human.Body.SetUpdateShapeBodyValue();
                human.Body.UpdateShapeBodyValue();
                human.Body.SetUpdateShapeBody();
                human.Body.UpdateShapeBody();
                human.Body._isUpdateShapeBody = false;
                entry.NativeShapeDirty = false;
            }
            entry.Original = original;
            // Retain separate current/previous snapshots until all native setters
            // finish; only then may the old buffers become scratch for a later call.
            entry.ReadBuffer = previousOriginal;
            entry.ApplyBuffer = ReferenceEquals(previous, original) ? null : previous;
            entry.AnimatorParametersDirty |= shapeChanged;
            return scope;
        }
        catch { End(human, scope); Release(human); throw; }
    }

    private static bool SourceMatches(HumanDataBody source, BodyChangeValues original)
    {
        if (original == null || !source.shapeValueBody.AsSpan().SequenceEqual(original.Shape)) return false;
        var extra = original.Extra;
        return source.bustSoftness == extra[0] && source.bustWeight == extra[1] &&
            source.areolaSize == extra[2] && source.upperArmSoftness == extra[3] &&
            source.bellySoftness == extra[4] && source.waistSoftness == extra[5] &&
            source.hipSoftness == extra[6] && source.thighSoftness == extra[7] &&
            source.nipColor.Equals(original.Nipple);
    }

    internal static void End(Human human, Scope scope)
    {
        scope?.Dispose();
        if (human != null && Entries.TryGetValue(human.Pointer, out var entry) && ReferenceEquals(entry.Active, scope))
            entry.Active = null;
    }

    private static void RefreshExtra(HumanBody body, BodyChangeValues current, BodyChangeValues previous, BodyChangeValues nativeSource = null)
    {
        bool Changed(int i) => current.Extra[i] != previous.Extra[i] ||
            (nativeSource != null && current.Extra[i] != nativeSource.Extra[i]);
        // The IL2CPP bridge unboxes BOTH Nullable arguments: C# null throws
        // before the native method runs. A boxed HasValue=false reads the body
        // data already in scope, with no absolute override or saved-data write.
        var fromBody = new Il2CppSystem.Nullable<float>();
        if (Changed(0)) body.ChangeBustSpring(fromBody, fromBody);
        if (Changed(1)) body.ChangeBustGravity(fromBody, fromBody);
        if (Changed(2)) body.ChangeSettingAreolaSize(fromBody, fromBody);
        if (Changed(3)) body.ChangeUpperArmSoftness(fromBody, fromBody);
        if (Changed(4)) body.ChangeBellySoftness(fromBody, fromBody);
        if (Changed(5)) body.ChangeWaistSoftness(fromBody, fromBody);
        if (Changed(6)) body.ChangeHipSoftness(fromBody, fromBody);
        if (Changed(7)) body.ChangeThighSoftness(fromBody, fromBody);
        if (current.Nipple != previous.Nipple || (nativeSource != null && current.Nipple != nativeSource.Nipple))
            body.ChangeSettingNipColor(new Il2CppSystem.Nullable<Color>());
    }

    internal static void Release(Human human)
    {
        if (human == null || !Entries.Remove(human.Pointer, out var entry)) return;
        entry.Active?.Dispose();
        try
        {
            if (!entry.Applied || human.Disposed || human.Body?.Pointer != entry.BodyPointer || human.Data?.Custom?.Body == null) return;
            var original = BodyChangeValues.Read(human.Data.Custom.Body);
            if (entry.Last != null) RefreshExtra(human.Body, original, entry.Last);
            human.Body._isUpdateShapeBodyValue = true;
            human.Body._isUpdateShapeBody = true;
            // Also restore an inactive actor which might not receive another native tick.
            human.Body.UpdateShapeBodyValue();
            human.Body.UpdateShapeBody();
            BodyChangeAnimation.RefreshAnimatorParameters(human);
        }
        catch (Exception ex) { PregnancyPlugin.Logger.LogWarning("[Body changes] Restore: " + ex.Message); }
    }
    internal static void ReleaseAll()
    { foreach (var entry in Entries.Values.ToArray()) Release(entry.Human); }
}

