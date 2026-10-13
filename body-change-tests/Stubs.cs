using System.Text.Json;
using ALPregnancy;

namespace UnityEngine
{
    public static class Time { public static float unscaledTime; }
    public sealed class GameObject : Object { public bool activeInHierarchy = true; public SkinnedMeshRenderer[] Renderers = Array.Empty<SkinnedMeshRenderer>(); public T[] GetComponentsInChildren<T>(bool inactive) => Renderers.Cast<T>().ToArray(); }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r=r; this.g=g; this.b=b; this.a=a; }
        public float this[int i] { get => i switch {0=>r,1=>g,2=>b,_=>a}; set {switch(i){case 0:r=value;break;case 1:g=value;break;case 2:b=value;break;default:a=value;break;}} }
        public static Color operator -(Color x, Color y) => new(x.r-y.r,x.g-y.g,x.b-y.b,x.a-y.a);
        public static bool operator ==(Color x, Color y) => x.r==y.r&&x.g==y.g&&x.b==y.b&&x.a==y.a;
        public static bool operator !=(Color x, Color y) => !(x==y);
        public override bool Equals(object obj) => obj is Color c && c==this;
        public override int GetHashCode() => HashCode.Combine(r,g,b,a);
    }
}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public sealed class Il2CppStructArray<T> : List<T> { public Il2CppStructArray(IEnumerable<T> data):base(data){} }
}
namespace Il2CppSystem
{
    public sealed class Nullable<T> where T : new()
    {
        public bool hasValue;
        public T value;
        public Nullable() { }
        public Nullable(T value) { this.value=value; hasValue=true; }
    }
}
namespace Character
{
    using UnityEngine;
    using Il2CppInterop.Runtime.InteropTypes.Arrays;
    public class NativeObject { private static int _next; public IntPtr Pointer = (IntPtr)Interlocked.Increment(ref _next); }
    public sealed class HumanDataBody : NativeObject
    {
        public Il2CppStructArray<float> shapeValueBody = new(new float[42]);
        public float bustSoftness,bustWeight,areolaSize,upperArmSoftness,bellySoftness,waistSoftness,hipSoftness,thighSoftness;
        public Color nipColor;
        public int skinId = 17; // Deliberately unrelated card content.
        public void Copy(HumanDataBody other)
        {
            // Deliberately shallow array copy to catch writes into the original.
            shapeValueBody = other.shapeValueBody;
            bustSoftness=other.bustSoftness;bustWeight=other.bustWeight;areolaSize=other.areolaSize;
            upperArmSoftness=other.upperArmSoftness;bellySoftness=other.bellySoftness;waistSoftness=other.waistSoftness;
            hipSoftness=other.hipSoftness;thighSoftness=other.thighSoftness;nipColor=other.nipColor;skinId=other.skinId;
        }
    }
    public sealed class HumanDataCustom { public HumanDataBody Body = new(); }
    public sealed class Parameter { public int sex=1; }
    public sealed class HumanData
    {
        public HumanDataCustom Custom = new(); public Parameter Parameter = new();
        public HumanData(int sex=1) { Parameter.sex=sex; }
        public static class LoadFileInfo { [Flags] public enum Flags { Custom=2,Parameter=8 } }
        public bool LoadCharaFile(string path, LoadFileInfo.Flags flags)
        {
            if (!File.Exists(path)) return false;
            Custom.Body=LoadFixture(Path.GetFileNameWithoutExtension(path)); return true;
        }
        public static HumanDataBody LoadFixture(string name)
        {
            using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixtures",name+".json")));
            var root=json.RootElement; var body=new HumanDataBody();
            var values=new BodyChangeValues {
                Shape=root.GetProperty("shapeValueBody").EnumerateArray().Select(v=>v.GetSingle()).ToArray(),
                Extra=BodyChangeValues.ExtraNames.Select(n=>root.GetProperty(n).GetSingle()).ToArray()
            };
            var c=root.GetProperty("nipColor").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
            values.Nipple=new Color(c[0],c[1],c[2],c[3]);values.Write(body);return body;
        }
    }
    public sealed class Human : NativeObject
    {
        public HumanData Data=new();public HumanBody Body;public bool Disposed,IsReloading,HiPoly=true;public int Sex=1;
        public GameObject GameObject=new();
        public Human() { Body=new(this); }
        public void Load() {} public void Reload() {} public void ReloadCoordinate() {} public void ReloadHead() {} public void ReloadHair() {} public void ReloadSkin() {} public void ReloadMannequin() {}
    }
    public sealed class HumanBody : NativeObject
    {
        public static class Define { public enum BodyShapeIdx { HipSizeY=41 } }
        public bool _isUpdateShapeBodyValue,_isUpdateShapeBody,ThrowExtra;
        public object SibBody=new(); public Human Human; public Human _human=>Human; public BodyEntry[] BodyRenderers = Array.Empty<BodyEntry>();
        internal BodyChangeValues Applied;public int ExtraCalls,ValueUpdates,ShapeUpdates;
        public readonly float[] AppliedExtras=new float[8];public Color AppliedNipple;
        public HumanBody(Human human) { Human=human; }
        public void SetUpdateShapeBodyValue() { _isUpdateShapeBodyValue=true; }
        public void SetUpdateShapeBody() { _isUpdateShapeBody=true; }
        public void UpdateShapeBodyValue() { Applied=BodyChangeValues.Read(Human.Data.Custom.Body);ValueUpdates++;_isUpdateShapeBodyValue=false; }
        public void UpdateShapeBody() { ShapeUpdates++; PhaseTrace.Events.Add("shape"); BodyChangeRuntime.NativeShapeUpdated(this); }
        public void LateUpdate()
        {
            if(_isUpdateShapeBodyValue)UpdateShapeBodyValue();
            if(_isUpdateShapeBody){UpdateShapeBody();_isUpdateShapeBody=false;}
        }
        private void Extra() { ExtraCalls++;if(ThrowExtra)throw new InvalidOperationException("Native test failure"); }
        private void Extra(int index, Il2CppSystem.Nullable<float> value, Il2CppSystem.Nullable<float> absValue)
        {
            // Match the real generated IL2CPP bridge, which unboxes BOTH arguments.
            if(value==null||absValue==null)throw new NullReferenceException("Il2CppObjectBaseToPtrNotNull: nullable argument");
            Extra();
            if(value.hasValue||absValue.hasValue)throw new Exception("Expected native read-from-body semantics, with no saved-data write or absolute override");
            AppliedExtras[index]=BodyChangeValues.Read(Human.Data.Custom.Body).Extra[index];
        }
        public void ChangeBustSpring(Il2CppSystem.Nullable<float> value=null,Il2CppSystem.Nullable<float> absValue=null)=>Extra(0,value,absValue);
        public void ChangeBustGravity(Il2CppSystem.Nullable<float> value=null,Il2CppSystem.Nullable<float> absValue=null)=>Extra(1,value,absValue);
        public bool ChangeSettingAreolaSize(Il2CppSystem.Nullable<float> value=null,Il2CppSystem.Nullable<float> absValue=null) { Extra(2,value,absValue);return true; }
        public void ChangeUpperArmSoftness(Il2CppSystem.Nullable<float> value=null,Il2CppSystem.Nullable<float> absValue=null)=>Extra(3,value,absValue);
        public void ChangeBellySoftness(Il2CppSystem.Nullable<float> value=null,Il2CppSystem.Nullable<float> absValue=null)=>Extra(4,value,absValue);
        public void ChangeWaistSoftness(Il2CppSystem.Nullable<float> value=null,Il2CppSystem.Nullable<float> absValue=null)=>Extra(5,value,absValue);
        public void ChangeHipSoftness(Il2CppSystem.Nullable<float> value=null,Il2CppSystem.Nullable<float> absValue=null)=>Extra(6,value,absValue);
        public void ChangeThighSoftness(Il2CppSystem.Nullable<float> value=null,Il2CppSystem.Nullable<float> absValue=null)=>Extra(7,value,absValue);
        public bool ChangeSettingNipColor(Il2CppSystem.Nullable<Color> color=null)
        {
            if(color==null)throw new NullReferenceException("Il2CppObjectBaseToPtrNotNull: color argument");
            Extra();
            if(color.hasValue)throw new Exception("Expected native read-from-body color semantics");
            AppliedNipple=Human.Data.Custom.Body.nipColor;return true;
        }
    }
}
namespace ALPregnancy
{
    internal sealed class Setting<T> { internal T Value;internal Setting(T value){Value=value;} }
    internal sealed class PregnancyPlugin
    {
        internal static Setting<bool> Enabled=new(true);
        internal static readonly Log Logger=new();
    }
    internal sealed class Log { internal void LogInfo(object value) { } internal void LogWarning(object value) { } internal void LogError(object value) { throw new Exception(value.ToString()); } }
    internal static class PregnancyConfig
    {
        internal static Setting<int> BodyBirths=new(5),BodyLower=new(0),BodyUpper=new(100);
        internal static Setting<float> NippleColorMaximum=new(.85f);
    }
    internal static class PregnancyController { internal static bool ManualPreviewActive; internal static void SuspendHuman(Character.Human human) {} internal static void AfterNativeUpdate(Character.Human human) { PhaseTrace.Events.Add("preview"); } }
    internal static class PregnancyRuntime {
        internal static readonly Dictionary<IntPtr,PregnancyRecord> Records=new();
        internal static PregnancyRecord FindVisualRecord(Character.Human human)=>PregnancyController.ManualPreviewActive?null:Records.GetValueOrDefault(human.Pointer);
        internal static void UpdateHumanVisual(Character.Human human) { PhaseTrace.Events.Add("belly"); PhaseTrace.Belly?.Invoke(human); } internal static void ReleaseHuman(Character.Human human) { ReleaseMeshes(human); BodyChangeRuntime.Release(human); } internal static int Releases;internal static void ReleaseMeshes(Character.Human human){Releases++;} }
}
