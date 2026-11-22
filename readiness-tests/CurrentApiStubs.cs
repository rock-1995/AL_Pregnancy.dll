namespace Character {
    public sealed class NativeArray<T> : List<T> {
        private static int next=10000;
        public IntPtr Pointer { get; }=(IntPtr)(++next);
        public int Length=>Count;
        public static implicit operator NativeArray<T>(T[] data) { var result=new NativeArray<T>(); result.AddRange(data); return result; }
    }
}
namespace ALPregnancy {
    [Flags] internal enum NativeClothingChange { None=0,Visibility=1,Topology=2 }
    internal static class NativeClothingChanges {
        private static readonly Dictionary<IntPtr,NativeClothingChange> Pending=new();
        internal static void Install(HarmonyLib.Harmony harmony) { }
        internal static void MarkTopology(Character.Human human) { if(human!=null) Pending[human.Pointer]=NativeClothingChange.Topology|NativeClothingChange.Visibility; }
        internal static NativeClothingChange Consume(Character.Human human)=>Pending.Remove(human.Pointer,out var value)?value:NativeClothingChange.None;
        internal static void Forget(Character.Human human)=>Pending.Remove(human.Pointer);
    }
}

namespace ALPregnancy { internal static class BellyVertexMorph { internal static bool IsClothingRenderer(UnityEngine.SkinnedMeshRenderer renderer)=>renderer.name=="shirt" || renderer.name.StartsWith("cloth"); } }
