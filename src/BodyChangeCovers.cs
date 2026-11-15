using Character;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace ALPregnancy;

// Clothing corrections contain absolute body-slider overrides. Keep their own
// baseline and garment selection, applying the same template delta as the body.
internal sealed class BodyChangeCovers
{
    private readonly HumanDataClothes _owner;
    private readonly Il2CppReferenceArray<HumanDataClothes.CoverInfo> _original, _working;
    private readonly Snapshot[] _snapshots;
    private readonly float[] _delta;
    private readonly double _progress;
    private readonly int _lower, _upper;
    internal bool HasChanges { get; }

    private sealed class Snapshot
    {
        internal IntPtr Pointer;
        internal bool Use;
        internal Dictionary<int, float> Values;
    }

    internal static HumanDataClothes Current(Human human) => human?.Cloth?._fileClothes;

    internal static BodyChangeCovers Prepare(BodyChangeCovers previous, HumanDataClothes owner,
        float[] delta, double progress, int lower, int upper)
    {
        if (owner?.coverInfos == null) return null;
        if (previous != null && ReferenceEquals(previous._delta, delta) &&
            previous._progress == progress && previous._lower == lower && previous._upper == upper &&
            previous.Matches(owner)) return previous;
        return new BodyChangeCovers(owner, delta, progress, lower, upper);
    }

    private BodyChangeCovers(HumanDataClothes owner, float[] delta, double progress, int lower, int upper)
    {
        _owner = owner; _original = owner.coverInfos;
        _delta = delta; _progress = progress; _lower = lower; _upper = upper;
        _snapshots = new Snapshot[_original.Length];
        for (int i = 0; i < _snapshots.Length; i++)
        {
            var source = _original[i];
            if (source == null) continue;
            var snapshot = _snapshots[i] = new Snapshot { Pointer = source.Pointer, Use = source.use };
            var table = source.infoTable;
            if (table != null)
            {
                snapshot.Values = new Dictionary<int, float>();
                foreach (var pair in table)
                {
                    int key = pair.Key;
                    float value = pair.Value;
                    snapshot.Values.Add(key, value);
                    float adjusted = source.use && key >= 0 && key < delta.Length && float.IsFinite(value)
                        ? BodyChangeMath.Add(value, delta[key], progress, lower, upper) : value;
                    HasChanges |= !adjusted.Equals(value);
                }
            }
        }
        // Empty, disabled or fully clamped corrections need no native copies.
        if (!HasChanges) return;
        _working = new Il2CppReferenceArray<HumanDataClothes.CoverInfo>(_original.Length);
        for (int i = 0; i < _snapshots.Length; i++)
        {
            var snapshot = _snapshots[i];
            if (snapshot == null) continue;
            var copy = new HumanDataClothes.CoverInfo { use = snapshot.Use };
            if (snapshot.Values == null) copy.infoTable = null;
            else
            {
                copy.infoTable = new Il2CppSystem.Collections.Generic.Dictionary<int, float>();
                foreach (var pair in snapshot.Values)
                    copy.infoTable.Add(pair.Key, snapshot.Use && pair.Key >= 0 && pair.Key < delta.Length && float.IsFinite(pair.Value)
                        ? BodyChangeMath.Add(pair.Value, delta[pair.Key], progress, lower, upper) : pair.Value);
            }
            _working[i] = copy;
        }
    }

    private bool Matches(HumanDataClothes owner)
    {
        if (!IsCurrent(owner)) return false;
        for (int i = 0; i < _snapshots.Length; i++)
        {
            var source = _original[i];
            var snapshot = _snapshots[i];
            if (source == null) { if (snapshot != null) return false; continue; }
            if (snapshot == null || source.Pointer != snapshot.Pointer || source.use != snapshot.Use) return false;
            var table = source.infoTable;
            if (table == null) { if (snapshot.Values != null) return false; continue; }
            if (snapshot.Values == null || table.Count != snapshot.Values.Count) return false;
            foreach (var pair in snapshot.Values)
                if (!table.TryGetValue(pair.Key, out float value) || !value.Equals(pair.Value)) return false;
        }
        return true;
    }

    private bool IsCurrent(HumanDataClothes owner) => owner?.Pointer == _owner.Pointer &&
        owner.coverInfos?.Pointer == _original.Pointer;

    // IK uses the last committed correction snapshot, just like the working body.
    // A coordinate reload must never borrow the previous outfit's correction.
    internal IDisposable Begin(HumanDataClothes current) =>
        HasChanges && IsCurrent(current) ? new Scope(_owner, _original, _working) : null;

    private sealed class Scope : IDisposable
    {
        private readonly HumanDataClothes _owner;
        private readonly Il2CppReferenceArray<HumanDataClothes.CoverInfo> _original, _working;
        private bool _disposed;
        internal Scope(HumanDataClothes owner, Il2CppReferenceArray<HumanDataClothes.CoverInfo> original,
            Il2CppReferenceArray<HumanDataClothes.CoverInfo> working)
        { _owner = owner; _original = original; _working = working; owner.coverInfos = working; }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_owner.coverInfos?.Pointer == _working.Pointer) _owner.coverInfos = _original;
        }
    }
}
