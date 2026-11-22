using Character;
using HarmonyLib;

namespace ALPregnancy;

// Customization extension in memory. The game's own body remains the clean base.
// Persistent gameplay values live in the matching external pregnancy save, not
// in the original PNG or ActorData._humanFileBinary. Native copies (including
// the maker's undo snapshots) receive independent values.
internal static class PregnancyAppearance
{
    private sealed class Entry
    {
        internal Il2CppSystem.WeakReference Owner;
        internal AppearanceValues Values;
    }
    private static readonly Dictionary<IntPtr, Entry> Entries = new();

    internal static AppearanceValues Get(HumanDataCustom custom)
    {
        if (custom == null) throw new ArgumentNullException(nameof(custom));
        if (Entries.TryGetValue(custom.Pointer, out var entry) && entry.Owner.Target?.Pointer == custom.Pointer)
            return entry.Values;
        var values = new AppearanceValues();
        Bind(custom, values);
        return values;
    }

    internal static void Bind(HumanDataCustom custom, AppearanceValues values)
    {
        if (custom == null || values == null) return;
        if (Entries.TryGetValue(custom.Pointer, out var entry) && entry.Owner.Target?.Pointer == custom.Pointer)
            entry.Values = values;
        else Entries[custom.Pointer] = new Entry { Owner = new Il2CppSystem.WeakReference(custom), Values = values };
    }

    internal static void Collect()
    {
        foreach (var key in Entries.Where(x => !x.Value.Owner.IsAlive).Select(x => x.Key).ToArray()) Entries.Remove(key);
    }

    internal static void Clear() => Entries.Clear();

    internal static void Install(Harmony harmony)
    {
        void Hook(Type type, string name, Type[] args, string before, string after)
        {
            var method = AccessTools.Method(type, name, args) ?? throw new MissingMethodException(type.FullName, name);
            harmony.Patch(method, before == null ? null : new HarmonyMethod(typeof(PregnancyAppearance), before),
                after == null ? null : new HarmonyMethod(typeof(PregnancyAppearance), after));
        }
        Hook(typeof(HumanDataCustom), nameof(HumanDataCustom.Copy), new[] { typeof(HumanDataCustom) }, nameof(BeforeCustomCopy), nameof(AfterCustomCopy));
        Hook(typeof(HumanData), nameof(HumanData.CopyCustom), new[] { typeof(HumanDataCustom) }, nameof(BeforeCustomCopy), nameof(AfterCopyCustom));
        Hook(typeof(HumanData), nameof(HumanData.Copy), new[] { typeof(HumanData), typeof(HumanData) }, nameof(BeforeFullCopy), nameof(AfterFullCopy));
        Hook(typeof(HumanData), nameof(HumanData.CopyWithoutPngData), new[] { typeof(HumanData) }, nameof(BeforeCopyWithoutPng), nameof(AfterCopyWithoutPng));
        Hook(typeof(HumanData), nameof(HumanData.CopyLimited), new[] { typeof(bool), typeof(HumanData), typeof(HumanData), typeof(HumanData.LoadLimited.Flags) }, nameof(BeforeLimitedCopy), nameof(AfterLimitedCopy));
        Hook(typeof(HumanDataCustom), nameof(HumanDataCustom.LoadBytes), new[] { typeof(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>), typeof(Il2CppSystem.Version) }, null, nameof(AfterLoadBytes));
        Hook(typeof(HumanData), nameof(HumanData.InitializeData), new[] { typeof(HumanData) }, null, nameof(AfterInitialize));
    }

    private static void BeforeCustomCopy(HumanDataCustom __0, out AppearanceValues __state) => __state = __0 == null ? new() : Get(__0).Clone();
    private static void AfterCustomCopy(HumanDataCustom __instance, AppearanceValues __state) => Bind(__instance, __state);
    private static void AfterCopyCustom(HumanData __instance, AppearanceValues __state) => Bind(__instance.Custom, __state);
    private static void BeforeFullCopy(HumanData src, out AppearanceValues __state) => __state = src?.Custom == null ? new() : Get(src.Custom).Clone();
    private static void AfterFullCopy(HumanData dst, AppearanceValues __state) => Bind(dst?.Custom, __state);
    private static void BeforeCopyWithoutPng(HumanData src, out AppearanceValues __state) => BeforeFullCopy(src, out __state);
    private static void AfterCopyWithoutPng(HumanData __instance, HumanData src, AppearanceValues __state)
    {
        Bind(__instance.Custom, __state);
        PregnancyMaker.OnDataCopied(__instance, src, __state);
    }
    private static void BeforeLimitedCopy(HumanData src, HumanData.LoadLimited.Flags flags, out AppearanceValues __state) =>
        __state = (flags & HumanData.LoadLimited.Flags.Body) != 0 && src?.Custom != null ? Get(src.Custom).Clone() : null;
    private static void AfterLimitedCopy(HumanData dst, AppearanceValues __state) { if (__state != null) Bind(dst?.Custom, __state); }
    private static void AfterLoadBytes(HumanDataCustom __instance, bool __result) { if (__result) Bind(__instance, new()); }
    private static void AfterInitialize(HumanData dst) => Bind(dst?.Custom, new());
}
