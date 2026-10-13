using System.Numerics;
using System.Text.Json;
using ALPregnancy;

internal static class SagRegression
{
    internal static void Run(Action<string,bool> check,string asset=null)
    {
        var torso=new TorsoProfile {PelvicFloor=0,Pubis=.1f,Navel=1.1f,Ribs=2.7f,SampleMin=0,SampleMax=3,
            Front=Enumerable.Repeat(.5f,21).ToArray(),Back=Enumerable.Repeat(-.5f,21).ToArray(),Width=Enumerable.Repeat(.9f,21).ToArray()};
        var points=(from x in new[]{-.5f,0,.5f} from y in Enumerable.Range(0,200) select new Vector3(x,.1f+y*.013f,.5f)).ToArray();
        Geometry(torso,points,check,"synthetic");
        var off=new VtxSettings {SagStrength=0,BellySag=0,NavelEversion=0,NavelProportion=0};
        var on=Copy(off);on.BellySag=1;
        float maxError=0;
        foreach(var p in points.Where(p=>p.X==0))
        {
            var a=BellyShape.Deform(p,torso,1,off);var b=BellyShape.Deform(p,torso,1,on);
            maxError=MathF.Max(maxError,MathF.Abs((a.Y-b.Y)-.075f*(a.Z-p.Z)));
        }
        check("New sag equals 0.075 times forward growth at strength one",maxError<2e-6f);
        var oldSettings=JsonSerializer.Deserialize<VtxSettings>("{\"sagStrength\":1.25}");
        check("Old preset keeps explicit surface slide and uses exported default for absent sag",oldSettings.SagStrength==1.25f && oldSettings.BellySag==2.5f);
        oldSettings.BellySag=1.3f;
        var restored=Copy(oldSettings);
        check("Both independent controls survive a JSON round trip",restored.SagStrength==1.25f && restored.BellySag==1.3f);
        ExtendedRange(torso,points,check);
        if(asset==null)return;
        using var doc=JsonDocument.Parse(File.ReadAllText(asset));
        var root=doc.RootElement;
        var frame=root.GetProperty("frame");
        Vector3 V(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle());
        var center=V(frame.GetProperty("center"));
        var right=V(frame.GetProperty("right"));
        var up=V(frame.GetProperty("up"));
        var forward=V(frame.GetProperty("forward"));
        var pointsFromMeshes=new List<Vector3>();
        foreach(var mesh in root.GetProperty("meshes").EnumerateArray())
        {
            if(!mesh.GetProperty("body").GetBoolean() || !mesh.GetProperty("active").GetBoolean())continue;
            var mapValue=mesh.GetProperty("toReferenceRowMajor");
            var map=mapValue.ValueKind==JsonValueKind.Null ? Matrix4x4.Identity : M(mapValue);
            foreach(var v in mesh.GetProperty("vertices").EnumerateArray())
            {
                var d=Vector3.Transform(V(v),map)-center;
                pointsFromMeshes.Add(new(Vector3.Dot(d,right),Vector3.Dot(d,up),Vector3.Dot(d,forward)));
            }
        }
        var anatomy=root.GetProperty("anatomy");
        var actual=TorsoProfile.Build(pointsFromMeshes,anatomy.GetProperty("PelvicFloor").GetSingle(),
            anatomy.GetProperty("Navel").GetSingle(),anatomy.GetProperty("SampleMax").GetSingle());
        check("AL capture contains active body vertices",pointsFromMeshes.Count>0);
        check("AL capture rebuilds the same torso span",MathF.Abs(actual.Span-anatomy.GetProperty("Span").GetSingle())<1e-5f);
        check("AL capture rebuilds the same front profile",actual.Front.Select((value,i)=>MathF.Abs(value-anatomy.GetProperty("Front")[i].GetSingle())).Max()<1e-5f);
        Geometry(actual,pointsFromMeshes.ToArray(),check,"AL capture");
        check("Actual AL navel detected for sag orientation test",float.IsFinite(actual.SkinNavelZ));
        Navel(actual,check);
        ExtendedRange(actual,pointsFromMeshes.ToArray(),check);
    }
    static void Geometry(TorsoProfile t,Vector3[] original,Action<string,bool> check,string label)
    {
        long comparisons=0;float maxDrop=0;
        foreach(float legacy in new[]{0f,.95983934f,2f})
        foreach(float stage in new[]{0f,1f/3,4f/9,.7f,1f})
        {
            var p=new VtxSettings {SagStrength=legacy,BellySag=0,HeightBoundaryArc=0};
            var before=original.Select(v=>PreSagBellyShape.Deform(v,t,stage,p)).ToArray();
            var current=original.Select(v=>BellyShape.Deform(v,t,stage,p)).ToArray();
            check($"{label}: bellySag zero exactly preserves 0.2.30 at legacy={legacy}, stage={stage}",before.SequenceEqual(current));
            foreach(float sag in new[]{.5f,1f,2f})
            {
                p.BellySag=sag;int builds=BellyShape.FieldBuildCount;
                bool finite=true,onlyY=true,down=true,identity=true;float drop=0;
                for(int i=0;i<original.Length;i++)
                {
                    var a=BellyShape.Deform(original[i],t,stage,p);var d=a-before[i];
                    finite&=float.IsFinite(a.X+a.Y+a.Z);onlyY&=d.X==0&&d.Z==0;
                    down&=d.Y<=1e-7f;identity&=a==original[i];drop=MathF.Max(drop,-d.Y);comparisons++;
                }
                check($"{label}: sag={sag}, legacy={legacy}, stage={stage}: finite vertical-only downward shift",finite&&onlyY&&down&&(stage!=0||identity));
                check($"{label}: new sag uses cached envelope",BellyShape.FieldBuildCount==builds);
                maxDrop=MathF.Max(maxDrop,drop);
            }
            if(stage==0)continue;
            var off=Copy(p);off.BellySag=0;
            Vector3 priorBefore=default,priorAfter=default;int reversals=0;bool forwardTangents=true;
            for(int k=0;k<=800;k++)
            {
                float y=t.PelvicFloor+(t.Ribs-t.PelvicFloor)*k/800;
                var v=new Vector3(0,y,t.FrontAt(y));
                var b=PreSagBellyShape.Deform(v,t,stage,off);var a=BellyShape.Deform(v,t,stage,p);
                if(k>0)
                {
                    if(b.Y>priorBefore.Y+1e-8f&&a.Y<=priorAfter.Y)reversals++;
                    forwardTangents &= Vector3.Dot(b-priorBefore,a-priorAfter)>0;
                }
                priorBefore=b;priorAfter=a;
            }
            check($"{label}: max new sag keeps meridian segments noncollapsed and forward, legacy={legacy}, stage={stage}",forwardTangents);
            // A deep synthetic belly can form a descending underside while
            // continuing forward in Z; Y alone does not detect a surface fold.
            // Also require monotone height on the actual AL body profiles.
            if(label=="AL capture")
                check($"{label}: max new sag introduces no meridian reversals, legacy={legacy}, stage={stage}",reversals==0);
        }
        check(label+": new sag has measurable full-term effect",maxDrop>t.Span*.005f);
        Console.WriteLine($"SAG {label}: {comparisons} vertex comparisons, peak new drop={maxDrop:R}, torsoSpan={t.Span:R}");
    }
    static void Navel(TorsoProfile t,Action<string,bool> check)
    {
        foreach(float legacy in new[]{0f,.95983934f,2f})
        foreach(float stage in new[]{.45f,.7f,1f})
        foreach(float sag in new[]{.5f,1f,2f})
        {
            var p=new VtxSettings {SagStrength=legacy,BellySag=0};
            float radius=p.NavelRadius*t.Span;
            var ring=Enumerable.Range(0,24).Select(k=>{float a=k*MathF.Tau/24,x=.95f*radius*MathF.Cos(a),y=t.SkinNavelY+1.3f*.95f*radius*MathF.Sin(a);return new Vector3(x,y,t.WallAt(x,y));}).ToArray();
            var before=ring.Select(v=>BellyShape.Deform(v,t,stage,p)).ToArray();p.BellySag=sag;
            var after=ring.Select(v=>BellyShape.Deform(v,t,stage,p)).ToArray();
            var delta=after[0]-before[0];float spread=after.Select((v,i)=>Vector3.Distance(v-before[i],delta)).Max();
            Vector3 Normal(Vector3[] a)=>Vector3.Normalize(Vector3.Cross(a[8]-a[0],a[16]-a[0]));
            check($"AL navel translates rigidly at legacy={legacy},stage={stage},sag={sag}",spread<t.Span*2e-6f);
            check($"AL navel orientation preserved at legacy={legacy},stage={stage},sag={sag}",Vector3.Distance(Normal(before),Normal(after))<2e-5f);
        }
    }
    static void ExtendedRange(TorsoProfile torso,Vector3[] points,Action<string,bool> check)
    {
        var p=new VtxSettings {BellySag=0};
        var before=points.Select(v=>BellyShape.Deform(v,torso,1,p)).ToArray();
        p.BellySag=1;
        var unit=points.Select(v=>BellyShape.Deform(v,torso,1,p)).ToArray();
        foreach(float sag in new[]{-1f,3f,5f,10f})
        {
            check($"Sag text accepts {sag} beyond the slider range",SagInput.TryRead(sag.ToString(System.Globalization.CultureInfo.InvariantCulture),out var typed)&&typed==sag);
            p.BellySag=sag;
            bool finite=true,linear=true,onlyY=true;
            float movement=0;
            for(int i=0;i<points.Length;i++)
            {
                var after=BellyShape.Deform(points[i],torso,1,p);
                finite &= float.IsFinite(after.X+after.Y+after.Z);
                onlyY &= after.X==before[i].X&&after.Z==before[i].Z;
                linear &= MathF.Abs(after.Y-before[i].Y-sag*(unit[i].Y-before[i].Y))<torso.Span*2e-6f;
                movement=MathF.Max(movement,MathF.Abs(after.Y-before[i].Y));
            }
            check($"Sag={sag} has uncapped linear vertical effect",finite&&linear&&onlyY&&movement>torso.Span*.005f);
            float current=sag;
            for(int i=0;i<100;i++)current=SagInput.FromSlider(current,SagInput.SliderPosition(current));
            check($"100 unchanged slider draws preserve typed sag={sag}",current==sag);
            check($"Typed sag={sag} survives JSON round trip",Copy(p).BellySag==sag);
        }
        check("Moving the slider deliberately returns to its chosen value",SagInput.FromSlider(5,1.25f)==1.25f);
        check("Incomplete and nonnumeric sag text cannot apply",new[]{"", "-", "1e", "abc", "NaN", "Infinity", "1e999"}.All(text=>!SagInput.TryRead(text,out _)));
    }
    static VtxSettings Copy(VtxSettings p)=>JsonSerializer.Deserialize<VtxSettings>(JsonSerializer.Serialize(p));
    static Matrix4x4 M(JsonElement m)=>new(m[0].GetSingle(),m[1].GetSingle(),m[2].GetSingle(),m[3].GetSingle(),m[4].GetSingle(),m[5].GetSingle(),m[6].GetSingle(),m[7].GetSingle(),m[8].GetSingle(),m[9].GetSingle(),m[10].GetSingle(),m[11].GetSingle(),m[12].GetSingle(),m[13].GetSingle(),m[14].GetSingle(),m[15].GetSingle());
}
