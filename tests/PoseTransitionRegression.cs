using ALPregnancy;
using System.Numerics;
using System.Text.Json;

internal static class PoseTransitionRegression
{
    internal static void Run(string input, string output, Action<string,bool> check)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(input));var root=doc.RootElement;
        var p=JsonSerializer.Deserialize<VtxSettings>(root.GetProperty("settings").GetRawText())!;
        var torso=JsonSerializer.Deserialize<TorsoProfile>(root.GetProperty("anatomy").GetRawText())!;
        var center=V(root.GetProperty("frame").GetProperty("center"));
        var up=V(root.GetProperty("frame").GetProperty("up"));
        var capture=Matrix(root.GetProperty("virtualAxis").GetProperty("transform"));
        var meshes=new List<object>();float oldDrop=0,newDrop=0;
        foreach(var m in root.GetProperty("meshes").EnumerateArray().Where(m=>m.GetProperty("active").GetBoolean() && m.GetProperty("virtualBoneIndex").GetInt32()>0))
        {
            string name=m.GetProperty("mesh").GetString()!;
            var original=Points(m,"vertices");var deformed=Points(m,"deformed");
            var alpha=m.GetProperty("virtualBlend").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            var native=m.GetProperty("skinToWorldRowMajor").EnumerateArray().Select(Matrix).ToArray();
            var names=m.GetProperty("boneNames").EnumerateArray().Select(x=>x.GetString()).ToArray();
            var w=Weights(m,"originalSkinWeights");var packed=Weights(m,"skinWeights");
            var toRef=Matrix(m.GetProperty("toReferenceRowMajor"));var vm=toRef*capture;
            Matrix4x4.Invert(native[Array.IndexOf(names,"cf_s_waist01")],out var inverse);
            Vector3 Skin(Vector3 v,VirtualWeights.Influence[] weights)=>weights.Aggregate(Vector3.Zero,(s,x)=>s+(x.Weight>0?Vector3.Transform(v,native[x.Bone])*x.Weight:Vector3.Zero));
            var before=new Vector3[original.Length];var after=new Vector3[original.Length];var adjusted=new float[original.Length];
            float error=0;bool unchangedUpper=true,finite=true;
            for(int i=0;i<original.Length;i++)
            {
                var o=Vector3.Transform(original[i],toRef);var n=Vector3.Transform(deformed[i],toRef);
                float y=Vector3.Dot(o-center,up);
                adjusted[i]=VirtualAxisMath.SurfaceWeight(Vector3.Distance(o,n),y,torso,p);
                var body=Skin(deformed[i],w[i]);var target=Vector3.Transform(deformed[i],vm);
                var oldWorld=Vector3.Lerp(body,target,alpha[i]);
                error=MathF.Max(error,Vector3.Distance(oldWorld,Skin(deformed[i],packed[i])));
                before[i]=Vector3.Transform(oldWorld,inverse);
                after[i]=Vector3.Transform(Vector3.Lerp(body,target,adjusted[i]),inverse);
                finite &= float.IsFinite(after[i].X+after[i].Y+after[i].Z) && adjusted[i]>=0 && adjusted[i]<=1;
                if(y>=torso.PelvicFloor+.62f*torso.Span) unchangedUpper &= MathF.Abs(adjusted[i]-alpha[i])<2e-5f;
            }
            check(name+": replay reproduces the captured GPU skin equation",error<2e-5f);
            check(name+": material weights and output remain finite",finite);
            check(name+": upper abdomen retains the captured virtual influence",unchangedUpper);
            int boneCount=m.GetProperty("virtualBoneIndex").GetInt32();
            var envelope=SkinBounds.Build(deformed,w,adjusted,boneCount);
            var box=envelope.Evaluate(i=>native[i],vm);
            check(name+": existing bounds formula encloses corrected pose",Enumerable.Range(0,original.Length).All(i=>box.Contains(Vector3.Transform(after[i],native[Array.IndexOf(names,"cf_s_waist01")]),1e-4f)));
            if(name.StartsWith("o_lower"))
            {
                var strip=Enumerable.Range(0,original.Length).Where(i=>MathF.Abs(original[i].X)<.04f && original[i].Z>.1f &&
                    original[i].Y>center.Y+torso.PelvicFloor+.06f*torso.Span && original[i].Y<center.Y-.05f*torso.Span)
                    .OrderBy(i=>original[i].Y).ToArray();
                for(int k=1;k<strip.Length;k++)
                {
                    if(original[strip[k]].Y-original[strip[k-1]].Y<1e-4f)continue;
                    oldDrop+=MathF.Max(0,before[strip[k-1]].Y-before[strip[k]].Y);
                    newDrop+=MathF.Max(0,after[strip[k-1]].Y-after[strip[k]].Y);
                }
            }
            meshes.Add(new {name,original=original.Select(C),before=before.Select(C),after=after.Select(C),alpha,adjusted,triangles=m.GetProperty("triangles").EnumerateArray().Select(x=>x.GetInt32()).ToArray()});
        }
        check("User capture reproduces the lower wall fold",oldDrop>.1f);
        check("Anatomical transition reduces captured lower midline reversal by more than half",newDrop<oldDrop*.5f);
        check("Unmoved skin has no new virtual influence",VirtualAxisMath.SurfaceWeight(0,0,torso,p)==0);
        check("Pelvic floor retains native attachment",VirtualAxisMath.SurfaceWeight(torso.Span,torso.PelvicFloor,torso,p)==0);
        p.VirtualAxisStrength=0;
        check("Virtual-axis off still disables all correction",VirtualAxisMath.SurfaceWeight(torso.Span,torso.Navel,torso,p)==0);
        Console.WriteLine($"POSE CAPTURE: lower midline reversal {oldDrop:F6} -> {newDrop:F6}; reduction={(1-newDrop/oldDrop)*100:F2}% (offline geometry, not a visual game test)");
        File.WriteAllText(output,JsonSerializer.Serialize(new {oldDrop,newDrop,meshes}));
    }
    private static VirtualWeights.Influence[][] Weights(JsonElement m,string key)=>m.GetProperty(key).EnumerateArray().Select(row=>Enumerable.Range(0,4).Select(k=>new VirtualWeights.Influence(row[k*2].GetInt32(),row[k*2+1].GetSingle())).ToArray()).ToArray();
    private static Vector3 V(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle());
    private static float[] C(Vector3 v)=>new[]{v.X,v.Y,v.Z};
    private static Vector3[] Points(JsonElement m,string key)=>m.GetProperty(key).EnumerateArray().Select(V).ToArray();
    private static Matrix4x4 Matrix(JsonElement e){var a=e.EnumerateArray().Select(x=>x.GetSingle()).ToArray();return new(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);}
}
