using Character;
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
        internal Scope Active;
        internal bool NativeShapeDirty;
        internal bool AnimatorParametersDirty;
    }

    // The saved body object and its arrays are never edited. The replacement exists
    // only during a synchronous native update and is removed even if it throws.
    internal sealed class Scope : IDisposable
    {
        private readonly HumanDataCustom _owner;
        private readonly HumanDataBody _original, _working;
        private bool _disposed;
        internal Scope(HumanDataCustom owner, HumanDataBody working)
        { _owner = owner; _original = owner.Body; _working = working; owner.Body = working; }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // A native reload is allowed to replace the entire body during the call.
            if (_owner.Body?.Pointer == _working.Pointer) _owner.Body = _original;
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

    internal static void ApplyAfterBelly(Human human, PregnancyRecord record)
    {
        var scope = BeginAfterBelly(human, record);
        End(human, scope);
        if (human != null && Entries.TryGetValue(human.Pointer, out var entry) && entry.AnimatorParametersDirty &&
            BodyChangeAnimation.RefreshAnimatorParameters(human)) entry.AnimatorParametersDirty = false;
    }

    // MotionIK reads Human.Data.Custom.Body, including through an inlined native
    // GetShapeBodyValue. Supply the last committed visual body only while native
    // contact correction runs, retaining its cover values and edition conversion.
    // Never refresh shape/physics or recompute pregnancy inside an IK callback.
    internal static Scope BeginAnimationRead(HumanBody body)
    {
        var human = body?._human;
        if (human == null || !PregnancyPlugin.Enabled.Value || PregnancyController.ManualPreviewActive ||
            human.Disposed || human.IsReloading || !human.HiPoly || human.Sex != 1 ||
            human.GameObject == null || !human.GameObject.activeInHierarchy || human.Body?.Pointer != body.Pointer ||
            !Entries.TryGetValue(human.Pointer, out var entry) || entry.BodyPointer != body.Pointer ||
            entry.Last == null || entry.Active != null)
            return null;
        var owner = human.Data?.Custom;
        if (owner?.Body == null) return null;
        var scope = new Scope(owner, entry.Working);
        entry.Active = scope;
        return scope;
    }

    internal static Scope BeginAfterBelly(Human human, PregnancyRecord record)
    {
        if (human == null) return null;
        if (Entries.TryGetValue(human.Pointer, out var running) && running.Active != null) return null;
        if (!PregnancyPlugin.Enabled.Value || PregnancyController.ManualPreviewActive || record == null ||
            human.Disposed || human.IsReloading || !human.HiPoly || human.Sex != 1 ||
            human.GameObject == null || !human.GameObject.activeInHierarchy || human.Body?.SibBody == null)
        { Release(human); return null; }
        RefreshCards();
        double progress = BodyChangeMath.Progress(record.IsPregnant, record.PregnancyProgress, record.Births, PregnancyConfig.BodyBirths.Value);
        if (_delta == null || progress == 0) { Release(human); return null; }
        var owner = human.Data?.Custom;
        if (owner?.Body?.shapeValueBody == null) return null;
        var original = BodyChangeValues.Read(owner.Body);
        original.Validate();
        var target = original.Apply(_delta, progress, PregnancyConfig.BodyLower.Value,
            PregnancyConfig.BodyUpper.Value, PregnancyConfig.NippleColorMaximum.Value);
        if (target.Same(original)) { Release(human); return null; }
        if (!Entries.TryGetValue(human.Pointer, out var entry) || entry.BodyPointer != human.Body.Pointer)
        {
            Release(human);
            Entries[human.Pointer] = entry = new Entry { Human = human, BodyPointer = human.Body.Pointer, Working = new HumanDataBody() };
        }
        if (entry.Active != null) return null; // Defensive against a reentrant native update.
        bool sourceChanged = !original.Same(entry.Original);
        bool nativeChanged = entry.NativeShapeDirty;
        bool changed = !target.Same(entry.Last) || sourceChanged || nativeChanged;
        bool shapeChanged = entry.Last == null || !target.Shape.SequenceEqual(entry.Last.Shape);
        entry.Working.Copy(owner.Body);
        target.Write(entry.Working);
        var scope = new Scope(owner, entry.Working);
        entry.Active = scope;
        var previous = entry.Last ?? original;
        // Record the attempted target before any native setter, so a partial native
        // failure restores every potentially touched parameter as well as the data.
        entry.Last = target;
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
            entry.AnimatorParametersDirty |= shapeChanged;
            return scope;
        }
        catch { End(human, scope); Release(human); throw; }
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
            if (human.Disposed || human.Body?.Pointer != entry.BodyPointer || human.Data?.Custom?.Body == null) return;
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
