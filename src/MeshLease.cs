using Character;
using UnityEngine;

namespace ALPregnancy;

// A preview owns its meshes. Original shared assets are never deformed.
internal sealed class MeshLease : IDisposable
{
    private static readonly HashSet<int> Owned = new();
    private static readonly Dictionary<int, Mesh> Originals = new();
    private sealed class SourceHold
    {
        public Mesh Mesh;
        public int Users;
        public bool AddedProtection;
    }
    private static readonly Dictionary<int, SourceHold> SourceHolds = new();
    private static readonly Dictionary<int, int> CloneSources = new();

    // The CoreCLR wrapper stored in Originals is not a native Unity asset root.
    // Protect the detached source before replacing the renderer reference. Keep
    // the hold through deferred FluidFlow retirement, including shared sources.
    private static void HoldSource(int cloneId, Mesh source)
    {
        int sourceId = source.GetInstanceID();
        if (!SourceHolds.TryGetValue(sourceId, out var hold))
        {
            bool added = (source.hideFlags & HideFlags.DontUnloadUnusedAsset) == 0;
            source.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            SourceHolds[sourceId] = hold = new SourceHold { Mesh = source, AddedProtection = added };
        }
        hold.Users++;
        CloneSources[cloneId] = sourceId;
    }
    internal static bool Owns(Mesh mesh) => mesh != null && Owned.Contains(mesh.GetInstanceID());
    internal static Mesh OriginalForReadiness(Mesh mesh) => mesh != null && Originals.TryGetValue(mesh.GetInstanceID(), out var original) ? original : mesh;
    internal static void ForgetOwnership(int meshId)
    {
        Owned.Remove(meshId); Originals.Remove(meshId);
        if (!CloneSources.Remove(meshId, out int sourceId) || !SourceHolds.TryGetValue(sourceId, out var hold)) return;
        if (--hold.Users > 0) return;
        SourceHolds.Remove(sourceId);
        if (hold.Mesh != null && hold.AddedProtection)
            hold.Mesh.hideFlags &= ~HideFlags.DontUnloadUnusedAsset;
    }
    private sealed class Entry
    {
        public SkinnedMeshRenderer Renderer;
        public Mesh Original;
        public Mesh Clone;
        public Bounds Bounds;
    }

    private readonly List<Entry> _entries = new();
    private readonly Dictionary<int, (int Mesh, bool Active)> _observed = new();
    private readonly Dictionary<int, Observation> _refresh = new();
    public int ReadableCount => _entries.Count;
    public int UnreadableCount { get; private set; }

    private static readonly string[] Excluded = { "face", "head", "eye", "hitomi", "namida", "canine", "hair", "tongue", "tooth", "teeth", "mayu", "o_acs_", "n_tang", "o_tang" };
    internal readonly record struct Observation(SkinnedMeshRenderer Renderer, Mesh Mesh, int RendererId, int MeshId, bool Active);
    internal readonly record struct Changes(bool Mesh, bool Visibility);
    internal static bool Relevant(SkinnedMeshRenderer renderer)
        => renderer != null && Relevant(renderer, renderer.sharedMesh);
    internal static bool Relevant(SkinnedMeshRenderer renderer, Mesh mesh)
    {
        if (renderer == null) return false;
        string name = (renderer.name + " " + mesh?.name).ToLowerInvariant();
        return !Excluded.Any(name.Contains);
    }

    // Reuse the completed native boundary's observations. A single pass checks
    // topology and consumes visibility; no hierarchy/name/property rescan.
    internal Changes CheckChanges(IReadOnlyList<Observation> observations)
    {
        if (observations == null) throw new InvalidOperationException("Mesh checks require the current actor's native boundary.");
        int count = 0; bool meshChanged = false, visibility = false;
        foreach (var item in observations)
        {
            if (item.Mesh == null) continue;
            count++;
            if (!_observed.TryGetValue(item.RendererId, out var old) || old.Mesh != item.MeshId)
            { meshChanged = true; continue; }
            if (old.Active == item.Active) continue;
            _observed[item.RendererId] = (old.Mesh, item.Active);
            visibility = true;
        }
        return new Changes(meshChanged || count != _observed.Count, visibility);
    }

    // A native clothes replacement leaves the other renderers and their owned
    // clones intact. Morph records for detached meshes are undone by the caller
    // before retiring these entries, just as they are before full Dispose.
    internal void Refresh(Human human, IReadOnlyList<Observation> observations)
    {
        _refresh.Clear();
        foreach (var item in observations)
            if (item.Renderer != null && item.Mesh != null) _refresh[item.RendererId] = item;
        int removed = 0, preserved = 0;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            if (entry.Renderer != null &&
                _refresh.TryGetValue(entry.Renderer.GetInstanceID(), out var item) && item.Mesh == entry.Clone)
            { preserved++; continue; }
            RestoreEntry(entry);
            _entries.RemoveAt(i);
            removed++;
        }
        var additions = new List<Observation>();
        foreach (var item in _refresh.Values)
            if (!_observed.TryGetValue(item.RendererId, out var old) || old.Mesh != item.MeshId)
                additions.Add(item);
        _observed.Clear();
        foreach (var item in _refresh.Values)
            _observed[item.RendererId] = (item.MeshId, item.Active);
        int before = _entries.Count;
        Acquire(human, additions);
        UnreadableCount = _observed.Count - _entries.Count;
        _refresh.Clear();
        PregnancyPlugin.Logger.LogInfo($"[Clothing meshes] preserved={preserved}, added={_entries.Count - before}, retired={removed}");
    }

    public void Acquire(Human human, IReadOnlyList<Observation> observations = null)
    {
        try
        {
            BodyMeshSelection.Register(human);
            IEnumerable<SkinnedMeshRenderer> renderers = observations != null
                ? observations.Select(o => o.Renderer) : human.GameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToArray();
            foreach (var renderer in renderers)
            {
                // Disposal may have restored a clone since the observation.
                // Read the live mesh here, but reuse the renderer selection.
                if (renderer == null || (observations == null && !Relevant(renderer)) || renderer.sharedMesh == null) continue;
                Mesh original = renderer.sharedMesh;
                Mesh clone = null;
                if (original.isReadable) clone = UnityEngine.Object.Instantiate(original);
                else if (BellyVertexMorph.IsClothingRenderer(renderer))
                {
                    clone = ReadableClothingMesh.TryCreate(original, out var reason);
                    if (clone == null)
                        PregnancyPlugin.Logger.LogWarning($"[Clothing] Cannot read {renderer.name}: {reason}");
                    else if (PregnancyPlugin.ConfigLog?.Value == true)
                        PregnancyPlugin.Logger.LogInfo($"[Clothing] Readable GPU copy: {renderer.name}, vertices={original.vertexCount}");
                }
                if (clone == null)
                {
                    UnreadableCount++;
                }
                else
                {
                    // Keep the name: the legacy morph uses mesh names to classify clothing.
                    clone.name = original.name;
                    clone.hideFlags = HideFlags.DontSave;
                    var entry = new Entry { Renderer = renderer, Original = original, Clone = clone, Bounds = renderer.localBounds };
                    _entries.Add(entry);
                    Owned.Add(clone.GetInstanceID());
                    Originals[clone.GetInstanceID()] = original;
                    HoldSource(clone.GetInstanceID(), original);
                    renderer.sharedMesh = clone;
                    if (PregnancyPlugin.ConfigLog?.Value == true)
                        PregnancyPlugin.Logger.LogInfo($"MeshLease source: renderer={renderer.name}, original={original.name}#{original.GetInstanceID()}, vertices={original.vertexCount}, clone={clone.GetInstanceID()}, sourceProtected={(original.hideFlags & HideFlags.DontUnloadUnusedAsset) != 0}");
                }
                _observed[renderer.GetInstanceID()] = (renderer.sharedMesh.GetInstanceID(), renderer.gameObject.activeInHierarchy && renderer.enabled);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var entry in _entries)
            RestoreEntry(entry);
        _entries.Clear();
        _observed.Clear();
        _refresh.Clear();
        UnreadableCount = 0;
        FluidMeshLifetime.Collect(force: true);
    }

    private static void RestoreEntry(Entry entry)
    {
            bool retainedBaseline = false;
            try
            {
                if (entry.Renderer != null && entry.Renderer.sharedMesh == entry.Clone)
                {
                    if (entry.Original != null) entry.Renderer.sharedMesh = entry.Original;
                    else
                    {
                        // The caller has already undone morph records. Forced
                        // asset destruction can bypass DontUnloadUnusedAsset;
                        // preserve this restored mesh as the renderer's baseline
                        // instead of replacing a live body with null.
                        retainedBaseline = true;
                        ForgetOwnership(entry.Clone.GetInstanceID());
                        entry.Clone.hideFlags &= ~HideFlags.DontUnloadUnusedAsset;
                        PregnancyPlugin.Logger.LogWarning($"[Mesh lifetime] Source was destroyed; retained restored mesh for {entry.Renderer.name} instead of assigning null.");
                    }
                    entry.Renderer.localBounds = entry.Bounds;
                }
            }
            catch (Exception ex) { PregnancyPlugin.Logger.LogWarning("Mesh restore: " + ex.Message); }
            finally { if (!retainedBaseline) FluidMeshLifetime.Retire(entry.Clone, entry.Original); }
    }
}
