using Character;
using HarmonyLib;

namespace ALPregnancy;

[Flags]
internal enum NativeClothingChange { None = 0, Visibility = 1, Topology = 2 }

// Coalesce the game's completed clothing changes until the Human's native
// completion boundary. Hooks only publish changes; they never touch meshes,
// shape data, physics, or the virtual-bone palette.
internal static class NativeClothingChanges
{
    private static readonly Dictionary<IntPtr, NativeClothingChange> Pending = new();

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(HumanCloth), nameof(HumanCloth.UpdateClothesStateAll), Type.EmptyTypes),
            postfix: new HarmonyMethod(typeof(NativeClothingChanges), nameof(AfterVisibilityCommit)) { priority = Priority.Last });
        string[] names = {
            nameof(HumanCloth.ChangeClothesTop), nameof(HumanCloth.ChangeClothesTopParts),
            nameof(HumanCloth.ChangeClothesBot), nameof(HumanCloth.ChangeClothesBra),
            nameof(HumanCloth.ChangeClothesShorts), nameof(HumanCloth.ChangeClothesGloves),
            nameof(HumanCloth.ChangeClothesPanst), nameof(HumanCloth.ChangeClothesSocks),
            nameof(HumanCloth.ChangeClothesShoes), nameof(HumanCloth.ChangeClothesArmAddParts),
            nameof(HumanCloth.ChangeClothesLegAddParts), nameof(HumanCloth.ChangeClothesOtherAddParts),
            nameof(HumanCloth.ChangeClothesAddParts)
        };
        // Include ChangeClothesTop(ref bool, ...): maker and coordinate changes
        // call it directly, bypassing the by-value convenience overload. In the
        // installed interop, ref bool maps to native byte&, not an IL2CPP boxed
        // value. These methods contain only int/bool arguments and this bool&.
        foreach (string name in names)
        {
            var methods = AccessTools.GetDeclaredMethods(typeof(HumanCloth)).Where(m => m.Name == name).ToArray();
            if (methods.Length == 0) throw new MissingMethodException(typeof(HumanCloth).FullName, name);
            foreach (var method in methods)
                harmony.Patch(method,
                    postfix: new HarmonyMethod(typeof(NativeClothingChanges), nameof(AfterTopologyCommit)) { priority = Priority.Last });
        }
    }

    private static void AfterVisibilityCommit(HumanCloth __instance) => Mark(__instance?._human, NativeClothingChange.Visibility);
    private static void AfterTopologyCommit(HumanCloth __instance) => MarkTopology(__instance?._human);

    internal static void MarkTopology(Human human) => Mark(human, NativeClothingChange.Topology | NativeClothingChange.Visibility);

    private static void Mark(Human human, NativeClothingChange change)
    {
        if (human == null) return;
        Pending.TryGetValue(human.Pointer, out var previous);
        Pending[human.Pointer] = previous | change;
    }

    internal static NativeClothingChange Consume(Human human) =>
        human != null && Pending.Remove(human.Pointer, out var change) ? change : NativeClothingChange.None;

    internal static void Forget(Human human)
    {
        if (human != null) Pending.Remove(human.Pointer);
    }
}
