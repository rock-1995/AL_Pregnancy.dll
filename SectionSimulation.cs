using ALPregnancy;
using System.Numerics;
using System.Text.Json;

internal static class SectionSimulation
{
    internal static void Export(string input,string output,Action<string,bool> check,bool upperMode=false)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(input));var root=doc.RootElement;
        var p=JsonSerializer.Deserialize<VtxSettings>(root.GetProperty("settings").GetRawText())!;
        var torso=JsonSerializer.Deserialize<TorsoProfile>(root.GetProperty("anatomy").GetRawText())!;
        var frame=root.GetProperty("frame"); var center=V(frame.GetProperty("center"));
        var up=V(frame.GetProperty("up"));var right=V(frame.GetProperty("right"));var forward=V(frame.GetProperty("forward"));
        var capture=M(root.GetProperty("virtualAxis").GetProperty("transform"));
        var active=root.GetProperty("meshes").EnumerateArray().Where(m=>m.GetProperty("active").GetBoolean() && m.GetProperty("body").GetBoolean() && m.GetProperty("virtualBoneIndex").GetInt32()>0).ToArray();
        var primary=active[0];var primaryNames=primary.GetProperty("boneNames").EnumerateArray().Select(x=>x.GetString()).ToArray();
        var pelvis=M(primary.GetProperty("skinToWorldRowMajor")[Array.IndexOf(primaryNames,"cf_s_waist01")]);
        Matrix4x4.Invert(pelvis,out var inverse);
        Vector3 Local(Vector3 world){var v=Vector3.Transform(world,inverse)-center;return new(Vector3.Dot(v,right)/torso.Span,Vector3.Dot(v,up)/torso.Span,Vector3.Dot(v,forward)/torso.Span);}
        float lo=(torso.PelvicFloor+.02f*torso.Span)/torso.Span,hi=torso.Ribs/torso.Span;
        float upperStart=MathF.Max(torso.Navel+.15f*torso.Span,torso.PelvicFloor+.62f*torso.Span)/torso.Span;
        var meshes=new List<object>();var samples=new List<object>();float captureError=0,baselineError=0;
        foreach(var m in active)
        {
            var original=Points(m,"vertices");var deformed=Points(m,"deformed");var native=m.GetProperty("skinToWorldRowMajor").EnumerateArray().Select(M).ToArray();
            var weights=W(m,"originalSkinWeights");var packed=W(m,"skinWeights");var toRef=M(m.GetProperty("toReferenceRowMajor"));var virtualMesh=toRef*capture;
            var oldAlpha=m.GetProperty("virtualBlend").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            Vector3 Skin(Vector3 v,VirtualWeights.Influence[] w)=>w.Aggregate(Vector3.Zero,(sum,k)=>sum+(k.Weight>0?Vector3.Transform(v,native[k.Bone])*k.Weight:Vector3.Zero));
            var n=new Vector3[original.Length];var v=new Vector3[original.Length];var weight=new float[original.Length];var u=new float[original.Length];var material=new float[original.Length];
            var materialZ=new float[original.Length];
            for(int i=0;i<n.Length;i++)
            {
                var o=Vector3.Transform(original[i],toRef);var d=Vector3.Transform(deformed[i],toRef);float y=Vector3.Dot(o-center,up);
                var nw=Skin(deformed[i],weights[i]);var vw=Vector3.Transform(deformed[i],virtualMesh);
                captureError=MathF.Max(captureError,Vector3.Distance(Vector3.Lerp(nw,vw,oldAlpha[i]),Skin(deformed[i],packed[i])));
                n[i]=Local(nw);v[i]=Local(vw);material[i]=y/torso.Span;
                materialZ[i]=Vector3.Dot(o-center,forward)/torso.Span;
                u[i]=Math.Clamp((y-torso.PelvicFloor-.02f*torso.Span)/(torso.Span*.60f),0,1);
                weight[i]=VirtualAxisMath.Weight(Vector3.Distance(o,d),torso.Span,p);
                p.LowerTransitionBias=0;
                float direct=VirtualAxisMath.SurfaceWeight(Vector3.Distance(o,d),y,torso,p);
                baselineError=MathF.Max(baselineError,MathF.Abs(direct-weight[i]*VirtualAxisMath.LowerAttachment(u[i],0)));
            }
            var triangles=m.GetProperty("triangles").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            var keep=new List<int[]>();var ids=new SortedSet<int>();
            for(int k=0;k<triangles.Length;k+=3)
            {
                var t=new[]{triangles[k],triangles[k+1],triangles[k+2]};
                // A blend is always between these endpoints: this conservative
                // plane test retains every triangle that can cross x=0 at any bias.
                if(t.Min(i=>MathF.Min(n[i].X,v[i].X))>1e-6f || t.Max(i=>MathF.Max(n[i].X,v[i].X)) < -1e-6f)continue;
                if(t.Max(i=>material[i])<lo || t.Min(i=>material[i])>hi)continue;
                keep.Add(t);foreach(int i in t)ids.Add(i);
            }
            var list=ids.ToArray();var remap=list.Select((id,index)=>(id,index)).ToDictionary(x=>x.id,x=>x.index);
            var rows=list.Select(i=>new[]{R(n[i].X),R(n[i].Y),R(n[i].Z),R(v[i].X),R(v[i].Y),R(v[i].Z),R(u[i]),R(weight[i]),R(material[i]),R(materialZ[i])}).ToArray();
            var faces=keep.Select(t=>t.Select(i=>remap[i]).ToArray()).ToArray();
            meshes.Add(new{name=m.GetProperty("mesh").GetString(),rows,faces,sourceIds=list});
            check(m.GetProperty("mesh").GetString()+": section keeps native triangle connectivity",rows.Length>10 && faces.All(t=>t.All(i=>i>=0 && i<rows.Length)));
            foreach(float bias in new[]{-2f,-1f,0f,1f,2f})
                foreach(int i in list.Where((_,k)=>k%7==0))
                {
                    float alpha=weight[i]*VirtualAxisMath.LowerAttachment(u[i],upperMode?0:bias);
                    if(upperMode)alpha=VirtualAxisMath.UpperAttachment(alpha,material[i]*torso.Span,torso,bias,p.VirtualAxisStrength);
                    samples.Add(new{mesh=m.GetProperty("mesh").GetString(),id=remap[i],bias,alpha,position=C(Vector3.Lerp(n[i],v[i],alpha))});
                }
        }
        check("Section replay reproduces captured packed skinning",captureError<2e-5f);
        check("Section zero-bias formula matches runtime SurfaceWeight",baselineError<1e-6f);
        var curves=new[]{-2f,-1f,0f,1f,2f}.Select(bias=>new{bias,values=Enumerable.Range(0,101).Select(i=>upperMode?VirtualAxisMath.UpperAttachment(i/100f,torso.Ribs,torso,bias):VirtualAxisMath.LowerAttachment(i/100f,bias)).ToArray()});
        File.WriteAllText(output,JsonSerializer.Serialize(new{sourceVersion=root.GetProperty("version").GetString(),rate=frame.GetProperty("rate").GetSingle(),span=torso.Span,lo,hi,upperStart,upperMode,strength=p.VirtualAxisStrength,meshes,curves,samples,captureError,baselineError}));
        Console.WriteLine($"SECTION: {meshes.Count} active body meshes; captureError={captureError:G6}, runtimeWeightError={baselineError:G6}.");
    }
    private static float R(float x)=>(float)Math.Round(x,7);
    private static float[] C(Vector3 x)=>new[]{x.X,x.Y,x.Z};
    private static Vector3 V(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle());
    private static Vector3[] Points(JsonElement m,string key)=>m.GetProperty(key).EnumerateArray().Select(V).ToArray();
    private static VirtualWeights.Influence[][] W(JsonElement m,string key)=>m.GetProperty(key).EnumerateArray().Select(row=>Enumerable.Range(0,4).Select(k=>new VirtualWeights.Influence(row[k*2].GetInt32(),row[k*2+1].GetSingle())).ToArray()).ToArray();
    private static Matrix4x4 M(JsonElement e){var a=e.EnumerateArray().Select(x=>x.GetSingle()).ToArray();return new(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);}
}
