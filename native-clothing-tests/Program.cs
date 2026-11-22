using System.Reflection;
using ALPregnancy;
using Character;
using HarmonyLib;

static void Check(bool test,string label) { if(!test)throw new Exception(label);Console.WriteLine("PASS "+label); }
static void Invoke(HumanCloth cloth,string name,int parameters)
{
    var hook=Harmony.Hooks.Single(h=>h.Original.Name==name&&h.Original.GetParameters().Length==parameters);
    hook.Postfix.Invoke(null,new object[]{cloth});
}
NativeClothingChanges.Install(new Harmony());
Check(Harmony.Hooks.Any(h=>h.Original.GetParameters().Any(p=>p.ParameterType==typeof(bool).MakeByRefType())),"actual ref-bool top overload is included");
Check(Harmony.Hooks.All(h=>h.Original.GetParameters().All(p=>p.ParameterType==typeof(int)||p.ParameterType==typeof(bool)||p.ParameterType==typeof(bool).MakeByRefType())),"all hook signatures use primitives or bool reference");
var a=new Human();var b=new Human();var cloth=new HumanCloth {_human=a};
Check(NativeClothingChanges.Consume(a)==NativeClothingChange.None,"unchanged native frame has no pending work");
Invoke(cloth,"UpdateClothesStateAll",0);
Check(NativeClothingChanges.Consume(a)==NativeClothingChange.Visibility,"state commit marks visibility without topology");
Check(NativeClothingChanges.Consume(a)==NativeClothingChange.None,"consume clears pending work");
Invoke(cloth,"ChangeClothesTop",6);Invoke(cloth,"ChangeClothesBot",2);Invoke(cloth,"UpdateClothesStateAll",0);
Check(NativeClothingChanges.Consume(a)==(NativeClothingChange.Visibility|NativeClothingChange.Topology),"nested clothing commits coalesce to one change");
NativeClothingChanges.MarkTopology(a);NativeClothingChanges.MarkTopology(b);NativeClothingChanges.Forget(a);
Check(NativeClothingChanges.Consume(a)==NativeClothingChange.None&&NativeClothingChanges.Consume(b).HasFlag(NativeClothingChange.Topology),"disposing one human preserves another's pending changes");
NativeClothingChanges.MarkTopology(null);NativeClothingChanges.Forget(null);
Check(NativeClothingChanges.Consume(null)==NativeClothingChange.None,"absent character has no pending changes");

namespace Character
{
    public sealed class Human { static long _id; public IntPtr Pointer {get;}=new(++_id); }
    public sealed class HumanCloth
    {
        public Human _human;
        public void UpdateClothesStateAll(){}
        public void ChangeClothesTop(int a,int b,int c,int d,bool e){}
        public void ChangeClothesTop(ref bool a,int b,int c,int d,int e,bool f){}
        public void ChangeClothesTopParts(int a,int b){}
        public void ChangeClothesTopParts(int a,int b,bool c){}
        public void ChangeClothesBot(int a,bool b){}
        public void ChangeClothesBra(int a,bool b){}
        public void ChangeClothesShorts(int a,bool b){}
        public void ChangeClothesGloves(int a,bool b){}
        public void ChangeClothesPanst(int a,bool b){}
        public void ChangeClothesSocks(int a,bool b){}
        public void ChangeClothesShoes(int a,bool b){}
        public void ChangeClothesArmAddParts(int a,bool b){}
        public void ChangeClothesLegAddParts(int a,bool b){}
        public void ChangeClothesOtherAddParts(int a,bool b){}
        public void ChangeClothesAddParts(int a,int b,bool c){}
    }
}
namespace HarmonyLib
{
    public static class Priority {public const int Last=0;}
    public sealed class HarmonyMethod
    {
        public int priority;public MethodInfo Method;
        public HarmonyMethod(Type t,string n){Method=t.GetMethod(n,BindingFlags.Static|BindingFlags.NonPublic);}
    }
    public static class AccessTools
    {
        public static MethodInfo Method(Type t,string n,Type[] args)=>t.GetMethod(n,args);
        public static IEnumerable<MethodInfo> GetDeclaredMethods(Type t)=>t.GetMethods(BindingFlags.Public|BindingFlags.DeclaredOnly|BindingFlags.Instance);
    }
    public sealed class Harmony
    {
        public static List<(MethodInfo Original,MethodInfo Postfix)> Hooks=new();
        public void Patch(MethodInfo original,HarmonyMethod postfix)=>Hooks.Add((original,postfix.Method));
    }
}
