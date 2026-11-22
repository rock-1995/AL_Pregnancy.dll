namespace Character {
    public sealed class NativeArray<T> : List<T> {
        private static int next=10000;
        public IntPtr Pointer { get; }=(IntPtr)(++next);
        public int Length=>Count;
        public static implicit operator NativeArray<T>(T[] data) { var result=new NativeArray<T>(); result.AddRange(data); return result; }
    }
}
namespace CharacterCreation.UI.View.Body.Softness { public class SoftnessEdit { public Character.HumanBody _humanBody; public void _CreateCategory_b__15_3(int i,float v) {} } }
namespace CharacterCreation.UI.View.Body.Breast { public class BreastEdit { public Character.HumanBody _humanBody; public void _CreateCategory_b__8_7(int i,float v) {} } }
namespace CharacterCreation.UI.View.Body.Breast.Nip { public class NipEdit { public Character.HumanBody _humanBody; public void _CreateCategory_b__8_4(int i,float v) {} } }
namespace ALPregnancy {
    // These adapter tests seed appearance from their existing pregnancy fixtures.
    // Separate appearance tests cover independent editor values and persistence.
    internal static class TestAppearance {
        internal static double Progress(PregnancyRecord record)=>BodyChangeMath.Progress(record.IsPregnant,record.PregnancyProgress,record.Births,PregnancyConfig.BodyBirths.Value);
        internal static void Apply(Character.Human human,PregnancyRecord record)=>BodyChangeRuntime.ApplyAfterBelly(human,Progress(record));
        internal static BodyChangeRuntime.Scope Begin(Character.Human human,PregnancyRecord record)=>BodyChangeRuntime.BeginAfterBelly(human,Progress(record));
    }
    internal static class PregnancyAppearance {
        internal static AppearanceValues Get(Character.HumanDataCustom custom) {
            var human=Character.Human.All.FirstOrDefault(h=>ReferenceEquals(h.Data.Custom,custom));
            var record=human==null?null:PregnancyRuntime.Records.GetValueOrDefault(human.Pointer);
            return new AppearanceValues { Body=record==null?0:(float)TestAppearance.Progress(record) };
        }
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
