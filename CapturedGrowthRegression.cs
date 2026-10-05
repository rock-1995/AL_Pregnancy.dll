using ALPregnancy;
using System.Numerics;
using System.Text.Json;

internal static class CapturedGrowthRegression
{
    internal static void Run(string input,string output,Action<string,bool> check)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(input));
        var meshes=document.RootElement.GetProperty("meshes").EnumerateArray().ToArray();
        var torso=meshes.Where(m=>m.GetProperty("body").GetBoolean() && m.GetProperty("active").GetBoolean()
            && !m.GetProperty("mesh").GetString()!.Contains("armleg")).ToArray();
        Vector3 Landmark(string name)
        {
            foreach(var m in torso)
            {
                var names=m.GetProperty("boneNames").EnumerateArray().Select(n=>n.GetString()).ToArray();
                int i=Array.IndexOf(names,name);
                if(i<0) continue;
                Matrix4x4.Invert(Matrix(m.GetProperty("bindposesRowMajor")[i]),out var rest);
                return Vector3.Transform(rest.Translation,Map(m));
            }
            throw new Exception("Missing captured landmark: "+name);
        }
        var origin=Landmark("cf_s_waist01");
        var floor=Landmark("cf_s_kokanskin")-origin;
        var chest=Landmark("cf_s_spine03")-origin;
        var all=torso.SelectMany(m=>Vertices(m,"vertices").Select(v=>v-origin)).ToArray();
        var profile=TorsoProfile.Build(all,floor.Y,0,chest.Y);
        check("Captured body builds a proportionate pelvic-to-rib profile", profile.Span>2.5f && profile.Span<3 && profile.Front.All(float.IsFinite));
        // Pin the original simulation preset; user-selected UI defaults may change.
        var settings=new VtxSettings {GrowthFullness=1,GrowthWidth=1,UpperReach=1,
            LateSettle=0,VerticalRange=1,WallSmoothing=1,SagStrength=1,
            MidVolume=1,LowerPoleLift=1,SkinClearance=.09f,NavelEversion=0,NavelProportion=0};
        float[] stages={0,1f/3,4f/9,5f/9,.75f,1};
        var results=new List<object>();
        float maxHeightError=0,minimumAreaRatio=float.MaxValue,maxDisplacement=0;
        string worstTriangle="";
        int changed=0,fullyProtected=0,earlyMoved=0;
        foreach(var m in torso)
        {
            var original=Vertices(m,"vertices");
            var old=Vertices(m,"deformed");
            var influence=Floats(m.GetProperty("influence"));
            var breast=m.GetProperty("breastWeights").ValueKind==JsonValueKind.Null ? new float[original.Length] : Floats(m.GetProperty("breastWeights"));
            var triangles=m.GetProperty("triangles").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            var deformed=new List<object>();
            foreach(float stage in stages)
            {
                var target=original.Select((v,i)=>
                {
                    var local=v-origin;
                    float amount=influence[i]*(1-BellyShape.BreastRestore(breast[i],1));
                    return v+(BellyShape.Deform(local,profile,stage,settings)-local)*amount;
                }).ToArray();
                check($"Captured {m.GetProperty("renderer").GetString()} stage {stage:F3}: finite and no backward push", target.Select((v,i)=>float.IsFinite(v.X+v.Y+v.Z) && v.Z>=original[i].Z-1e-6f && v.Y<=original[i].Y+1e-6f).All(x=>x));
                if(stage==0) check("Captured zero stage is exact identity",original.SequenceEqual(target));
                if(stage==1f/3) earlyMoved+=target.Where((v,i)=>Vector3.Distance(v,original[i])>1e-6f).Count();
                for(int i=0;i<target.Length;i++)
                {
                    maxHeightError=Math.Max(maxHeightError,Math.Abs(target[i].Y-original[i].Y));
                    if(breast[i]>=.99999f)
                    {
                        if(Vector3.Distance(target[i],original[i])>1e-6f) throw new Exception("Breast guard moved a fully protected captured vertex.");
                        fullyProtected++;
                    }
                    float displacement=Vector3.Distance(target[i],original[i]);
                    maxDisplacement=Math.Max(maxDisplacement,displacement);
                    if(stage==1 && displacement>1e-5f) changed++;
                }
                for(int i=0;i<triangles.Length;i+=3)
                {
                    int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                    float before=Vector3.Cross(original[b]-original[a],original[c]-original[a]).Length();
                    if(before<1e-7f) continue;
                    float after=Vector3.Cross(target[b]-target[a],target[c]-target[a]).Length();
                    if(after/before<minimumAreaRatio)
                    {
                        minimumAreaRatio=after/before;
                        worstTriangle=$"stage={stage}, mesh={m.GetProperty("renderer").GetString()}, indices={a},{b},{c}, before={original[a]};{original[b]};{original[c]}, after={target[a]};{target[b]};{target[c]}, area={before:G4}";
                    }
                }
                deformed.Add(new {stage,vertices=target.Select(Components).ToArray()});
            }
            results.Add(new {name=m.GetProperty("renderer").GetString(),original=original.Select(Components).ToArray(),previous=old.Select(Components).ToArray(),triangles,stages=deformed});
        }
        check("Captured reference-preset sag is downward with bounded height displacement",maxHeightError>.1f && maxHeightError<.7f);
        check("Captured pure breast vertices remain fixed",fullyProtected>100);
        check("Captured late stage actually expands the abdomen",changed>300 && maxDisplacement>.5f && maxDisplacement<2);
        check("Captured early stage remains inside the abdominal wall",earlyMoved==0);
        Console.WriteLine($"CAPTURED: span={profile.Span:F5}, changedLate={changed}, maxDisplacement={maxDisplacement:F5}, maxHeightError={maxHeightError:G5}, minTriangleAreaRatio={minimumAreaRatio:F5}");
        Console.WriteLine("SMALLEST AREA RATIO: "+worstTriangle);
        if(!string.IsNullOrEmpty(output))
            File.WriteAllText(output,JsonSerializer.Serialize(new {origin=Components(origin),profile,settings,eggs=stages.Select(stage=>new {stage,egg=BellyShape.Growth(profile,stage,settings)}).ToArray(),meshes=results}));
        check("Captured mesh triangles do not collapse",minimumAreaRatio>.2f);
    }
    private static Vector3[] Vertices(JsonElement m,string key)
        => m.GetProperty(key).EnumerateArray().Select(v=>Vector3.Transform(new Vector3(v[0].GetSingle(),v[1].GetSingle(),v[2].GetSingle()),Map(m))).ToArray();
    private static float[] Floats(JsonElement e)=>e.EnumerateArray().Select(x=>x.GetSingle()).ToArray();
    private static float[] Components(Vector3 v)=>new[]{v.X,v.Y,v.Z};
    private static Matrix4x4 Map(JsonElement m)=>m.GetProperty("toReferenceRowMajor").ValueKind==JsonValueKind.Null ? Matrix4x4.Identity : Matrix(m.GetProperty("toReferenceRowMajor"));
    private static Matrix4x4 Matrix(JsonElement e)
    {
        var a=Floats(e);
        return new Matrix4x4(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);
    }
}

