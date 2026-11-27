using ALPregnancy;
using UnityEngine;

BellyVertexMorph.RunCacheTests();

namespace ALPregnancy
{
    internal static partial class BellyVertexMorph
    {
        private sealed class RecordFixture { internal NearestBodyVertexMap Map; }
        private sealed class EntryFixture { internal RecordFixture Record; internal bool Visible; }
        static int Cases;
        static void Require(bool value,string message){if(!value)throw new Exception("FAIL: "+message);}
        static void Case(string name,Action test){test();Cases++;Console.WriteLine("PASS "+name);}
        static Vector3[] Overlay()=>new[]{new Vector3(.1f,.2f,.3f),new Vector3(1.2f,-.1f,.1f),new Vector3(-2,.5f,.2f)};
        static Vector3[] Body()=>new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(-1,1,0),new Vector3(-3,0,0),new Vector3(0,0,3)};
        static int[] Old(Vector3[] overlay,Vector3[] body,Matrix4x4 matrix)
        {
            var transformed=new Vector3[overlay.Length];
            for(int i=0;i<transformed.Length;i++)transformed[i]=matrix.MultiplyPoint3x4(overlay[i]);
            return BuildNearestBodyVertCacheBaseline(transformed,body);
        }
        static int[] Miss(NearestBodyVertexMap cache,Vector3[] overlay,Vector3[] body,Matrix4x4 matrix,int[] previous=null)
        {
            Probe.Reset();var expected=Old(overlay,body,matrix);
            Probe.Reset();var actual=cache.Get(overlay,body,matrix);
            Require(actual.SequenceEqual(expected),"mapping differs from original method");
            Require(Probe.Transforms==overlay.Length,"miss transformation count");
            Require(Probe.Distances==0,"nearest search still invokes native vector distance wrappers");
            if(previous!=null)Require(!ReferenceEquals(actual,previous),"miss returned previous table");
            return actual;
        }
        static void Hit(NearestBodyVertexMap cache,Vector3[] overlay,Vector3[] body,Matrix4x4 matrix,int[] expected)
        {
            Probe.Reset();var actual=cache.Get(overlay,body,matrix);
            Require(ReferenceEquals(actual,expected),"hit changed table identity");
            Require(Probe.Transforms==0&&Probe.Distances==0,"hit entered transformation/search");
        }
        internal static void RunCacheTests()
        {
            Case("Cold correspondence then 1000 same-key calls perform zero transforms/searches",()=>{
                var a=Overlay();var b=Body();var cache=new NearestBodyVertexMap();var first=Miss(cache,a,b,Matrix4x4.identity);
                for(int i=0;i<1000;i++)Hit(cache,a,b,Matrix4x4.identity,first);
            });
            Case("Overlay and body same-length baseline replacements each invalidate by identity",()=>{
                var a=Overlay();var b=Body();var cache=new NearestBodyVertexMap();var current=Miss(cache,a,b,Matrix4x4.identity);
                a=(Vector3[])a.Clone();current=Miss(cache,a,b,Matrix4x4.identity,current);
                b=(Vector3[])b.Clone();current=Miss(cache,a,b,Matrix4x4.identity,current);
                a=new[]{new Vector3(9,0,0),new Vector3(-9,0,0),new Vector3(0,4,0)};
                current=Miss(cache,a,b,Matrix4x4.identity,current);
                b=new[]{new Vector3(10,0,0),new Vector3(-10,0,0),new Vector3(0,4,0),new Vector3(0,-9,0),new Vector3(0,0,-9)};
                current=Miss(cache,a,b,Matrix4x4.identity,current);Hit(cache,a,b,Matrix4x4.identity,current);
            });
            Case("Body switch and return, changed lengths and simultaneous replacements remain exact",()=>{
                var a=Overlay();var b=Body();var other=new[]{new Vector3(50,0,0)};var cache=new NearestBodyVertexMap();
                var first=Miss(cache,a,b,Matrix4x4.identity);var second=Miss(cache,a,other,Matrix4x4.identity,first);
                var third=Miss(cache,a,b,Matrix4x4.identity,second);
                Miss(cache,new[]{new Vector3(1,1,1)},new[]{new Vector3(4,4,4),new Vector3(0,0,0)},Matrix4x4.identity,third);
            });
            Case("Every matrix component: equal copy hits; one-ULP change misses, including fourth row",()=>{
                var a=Overlay();var b=Body();var matrix=new Matrix4x4();for(int i=0;i<16;i++)matrix[i]=i+1;
                for(int i=0;i<16;i++){
                    var cache=new NearestBodyVertexMap();var before=Miss(cache,a,b,matrix);var same=matrix;Hit(cache,a,b,same,before);
                    var changed=matrix;changed[i]=MathF.BitIncrement(changed[i]);
                    var after=Miss(cache,a,b,changed,before);Hit(cache,a,b,changed,after);Miss(cache,a,b,matrix,after);
                }
            });
            Case("Exact numeric Matrix.Equals semantics: signed zeros equal, nonfinite behavior matches Numerics",()=>{
                var a=Overlay();var b=Body();var matrix=Matrix4x4.identity;var cache=new NearestBodyVertexMap();var before=Miss(cache,a,b,matrix);
                matrix.m01=BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));Hit(cache,a,b,matrix,before);
                matrix.m30=float.NaN;var after=Miss(cache,a,b,matrix,before);Probe.Reset();var again=cache.Get(a,b,matrix);
                bool numericsEqual=MatrixBridge.ToManaged(matrix).Equals(MatrixBridge.ToManaged(matrix));
                Require(ReferenceEquals(after,again)==numericsEqual,"matrix equality diverged from exact Numerics contract");
                Require(Probe.Distances==0,"nonfinite matrix path calls native distance wrappers");
            });
            Case("Entry reconstruction and visibility changes preserve longer-lived record cache",()=>{
                var a=Overlay();var b=Body();var record=new RecordFixture();var entry=new EntryFixture{Record=record,Visible=true};
                var first=Miss(entry.Record.Map??=new NearestBodyVertexMap(),a,b,Matrix4x4.identity);
                for(int i=0;i<20;i++){entry=new EntryFixture{Record=record,Visible=(i&1)==0};Hit(entry.Record.Map,a,b,Matrix4x4.identity,first);}
            });
            Case("Separate record cache objects never share correspondence ownership",()=>{
                var a=Overlay();var b=Body();var left=new NearestBodyVertexMap();var right=new NearestBodyVertexMap();
                var l=Miss(left,a,b,Matrix4x4.identity);var r=Miss(right,a,b,Matrix4x4.identity);
                Require(!ReferenceEquals(l,r),"distinct caches share table");Hit(left,a,b,Matrix4x4.identity,l);Hit(right,a,b,Matrix4x4.identity,r);
            });
            Case("Equal-distance and duplicated body points keep original first-index tie",()=>{
                var a=new[]{new Vector3(0,0,0),new Vector3(1,0,0)};
                var b=new[]{new Vector3(-1,0,0),new Vector3(1,0,0),new Vector3(1,0,0)};
                var result=Miss(new NearestBodyVertexMap(),a,b,Matrix4x4.identity);Require(result.SequenceEqual(new[]{0,1}),"first-index tie changed");
            });
            Case("Empty overlay and empty body retain the old helper behavior without new rejection",()=>{
                var cache=new NearestBodyVertexMap();var empty=Array.Empty<Vector3>();var body=Body();
                var result=Miss(cache,empty,body,Matrix4x4.identity);Hit(cache,empty,body,Matrix4x4.identity,result);
                result=Miss(new NearestBodyVertexMap(),Overlay(),empty,Matrix4x4.identity);Require(result.All(i=>i==0),"empty body legacy behavior changed");
            });
            Case("Random, translated, rotated, scaled and sheared inputs equal old mapping in both body orders",()=>{
                var random=new Random(41721);
                float F()=> (float)(random.NextDouble()*40-20);
                for(int trial=0;trial<80;trial++){
                    var a=Enumerable.Range(0,trial%31).Select(_=>new Vector3(F(),F(),F())).ToArray();
                    var b=Enumerable.Range(0,1+trial%43).Select(_=>new Vector3(F(),F(),F())).ToArray();
                    var nm=System.Numerics.Matrix4x4.CreateScale(.3f+(trial%5),1.2f,.7f)*System.Numerics.Matrix4x4.CreateRotationZ(trial*.19f)*System.Numerics.Matrix4x4.CreateTranslation(F(),F(),F());
                    nm.M21+=.13f;var matrix=MatrixBridge.ToUnity(nm);var cache=new NearestBodyVertexMap();
                    var result=Miss(cache,a,b,matrix);Hit(cache,a,b,matrix,result);
                    var reverse=b.Reverse().ToArray();result=Miss(cache,a,reverse,matrix,result);Hit(cache,a,reverse,matrix,result);
                }
            });
            Case("Transform exception never publishes new key or corrupts previous successful table",()=>Failure(false));
            Console.WriteLine($"PASS {Cases} scenario groups; production cache linked directly; nearest method extracted verbatim for cached and uncached comparisons.");
            Console.WriteLine("Fixture arithmetic/counters and cache lifecycle only; no Unity native calls, game launch, real renderer/visual checks, or measured game FPS.");
        }
        static void Failure(bool inSearch)
        {
            var a=Overlay();var b=Body();var cache=new NearestBodyVertexMap();var initial=Miss(cache,a,b,Matrix4x4.identity);var saved=(int[])initial.Clone();
            var changed=(Vector3[])a.Clone();changed[0]=new Vector3(200,0,0);var matrix=Matrix4x4.identity;matrix.m03=1;
            Probe.Reset();if(inSearch)Probe.ThrowDistance=2;else Probe.ThrowTransform=2;
            bool threw=false;try{cache.Get(changed,b,matrix);}catch(InvalidOperationException){threw=true;}
            Require(threw,"fault injection did not execute");Require(initial.SequenceEqual(saved),"old table altered by failed rebuild");
            Hit(cache,a,b,Matrix4x4.identity,initial);
            var next=Miss(cache,changed,b,matrix,initial);Hit(cache,changed,b,matrix,next);
        }
    }
}
