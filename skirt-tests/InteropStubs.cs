namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppStructArray<T>
    {
        readonly T[] values;
        public Il2CppStructArray(T[] v) { values=(T[])v.Clone(); }
        public T this[int i] { get=>values[i]; set=>values[i]=value; }
        public static implicit operator T[](Il2CppStructArray<T> a)=>(T[])a.values.Clone();
    }
}
