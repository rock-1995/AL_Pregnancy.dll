namespace UnityEngine
{
    [Flags] public enum HideFlags { None = 0, DontSave = 1, DontUnloadUnusedAsset = 2 }
    public class Object
    {
        private static int _next;
        private readonly int _id = ++_next;
        public string name;
        public HideFlags hideFlags;
        public bool Destroyed;
        public int GetInstanceID() => _id;
        public static int Clones;
        public static Mesh Instantiate(Mesh source)
        {
            Clones++;
            return new Mesh { name = source.name, isReadable = source.isReadable, vertexCount = source.vertexCount, hideFlags = source.hideFlags };
        }
        public static void Destroy(Object item) { if (!ReferenceEquals(item, null)) item.Destroyed = true; }
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => this == other as Object;
        public override int GetHashCode() => _id;
    }
    public class Mesh : Object { public bool isReadable = true; public int vertexCount = 100; public Mesh ReadbackResult; }
    public readonly record struct Bounds(int Value);
    public class GameObject : Object
    {
        public bool activeInHierarchy = true;
        public readonly List<SkinnedMeshRenderer> Renderers = new();
        public T[] GetComponentsInChildren<T>(bool includeInactive) => Renderers.Cast<T>().ToArray();
    }
    public class SkinnedMeshRenderer : Object
    {
        public Mesh sharedMesh;
        public bool enabled = true;
        public GameObject gameObject = new();
        public Bounds localBounds;
        // Explicit classifier answers; these tests exercise lease routing, not
        // a reimplementation of the production name/path classifier.
        public bool ClothingEligible;
        public Character.Human ExpectedActor;
    }
}
namespace Character { public class Human { public UnityEngine.GameObject GameObject = new(); } }
namespace ALPregnancy
{
    internal static class PregnancyPlugin
    {
        public sealed class Config { public bool Value = false; }
        public static readonly Config ConfigLog = new();
        public sealed class Log { public readonly List<string> Lines = new(); public void LogInfo(string value) => Lines.Add(value); public void LogWarning(string value) => Lines.Add(value); }
        public static readonly Log Logger = new();
    }
    internal static class BodyMeshSelection
    {
        internal static Character.Human Registered;
        internal static int Registrations;
        internal static void Register(Character.Human human) { Registered=human; Registrations++; }
    }
    internal static class BellyVertexMorph
    {
        internal static int Classifications;
        internal static bool IsClothingRenderer(UnityEngine.SkinnedMeshRenderer renderer)
        {
            Classifications++;
            if (renderer.ExpectedActor != null && !ReferenceEquals(renderer.ExpectedActor,BodyMeshSelection.Registered))
                throw new Exception("Clothing classification used another actor's body registration.");
            return renderer.ClothingEligible;
        }
    }
    internal static class ReadableClothingMesh
    {
        public static int Attempts;
        public static UnityEngine.Mesh TryCreate(UnityEngine.Mesh original, out string reason)
        { Attempts++; reason = original.ReadbackResult == null ? "stub GPU unavailable" : null; return original.ReadbackResult; }
    }
    // Native FluidFlow references are outside this unit boundary. Keep retirement
    // deferred and call production ForgetOwnership only when collection happens.
    internal static class FluidMeshLifetime
    {
        public static readonly List<(UnityEngine.Mesh Clone, UnityEngine.Mesh Original)> Pending = new();
        public static void Retire(UnityEngine.Mesh clone, UnityEngine.Mesh original) => Pending.Add((clone, original));
        public static void Collect(bool force = false)
        {
            foreach (var item in Pending)
            {
                MeshLease.ForgetOwnership(item.Clone.GetInstanceID());
                UnityEngine.Object.Destroy(item.Clone);
            }
            Pending.Clear();
        }
    }
}
