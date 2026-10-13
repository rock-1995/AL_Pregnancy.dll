namespace ALPregnancy;
internal static class PhaseTrace
{
    internal static readonly List<string> Events=new();
    internal static Action<Character.Human> Belly,Binding;
}
internal static class BellyVertexMorph
{
    internal static void UpdateVirtual(Character.Human human) { PhaseTrace.Events.Add("binding");PhaseTrace.Binding?.Invoke(human); }
    internal static void UpdateBodyCollision(Character.Human human) { PhaseTrace.Events.Add("collision"); }
}
