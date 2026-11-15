using ALPregnancy;
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public sealed class Il2CppReferenceArray<T> : Character.NativeObject
    {
        public static int Allocations;
        private readonly T[] _items;
        public int Length => _items.Length;
        public T this[int i] { get => _items[i]; set => _items[i] = value; }
        public Il2CppReferenceArray(long length) { _items = new T[length]; Allocations++; }
    }
}
namespace Il2CppSystem.Collections.Generic
{
    public sealed class Dictionary<K,V> : System.Collections.Generic.Dictionary<K,V> { }
}
namespace Character
{
    using Il2CppInterop.Runtime.InteropTypes.Arrays;
    public sealed class HumanDataClothes : NativeObject
    {
        public Il2CppReferenceArray<CoverInfo> coverInfos;
        public sealed class CoverInfo : NativeObject
        {
            public bool use;
            public Il2CppSystem.Collections.Generic.Dictionary<int,float> infoTable = new();
        }
    }
    public sealed class HumanDataCoordinate { public HumanDataClothes Clothes = new(); }
    public sealed class HumanCloth
    {
        public HumanDataClothes _fileClothes = new();
        // Stand in for the native garment state / precedence selection. The
        // plugin must leave this selection alone and only change its input data.
        public int VisibleGarment;
        public float Shape(Human human, int key)
        {
            var covers = _fileClothes?.coverInfos;
            if (VisibleGarment >= 0 && covers != null && VisibleGarment < covers.Length)
            {
                var cover = covers[VisibleGarment];
                if (cover?.use == true && cover.infoTable != null && cover.infoTable.TryGetValue(key, out float value)) return value;
            }
            return human.Data.Custom.Body.shapeValueBody[key];
        }
        internal void Apply(Human human, BodyChangeValues values)
        { for (int i=0; i<values.Shape.Length; i++) values.Shape[i] = Shape(human, i); }
    }
}
