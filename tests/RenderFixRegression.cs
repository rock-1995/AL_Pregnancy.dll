using ALPregnancy;
using System.Numerics;
using System.Text.Json;

internal static class RenderFixRegression
{
    internal static void Run(Action<string,bool> check)
    {
        // Two unchanged face orientations, but very different triangle areas.
        Vector3[] points={new(0,0,0),new(1,0,0),new(0,1,0),new(-1,0,0)};
        Vector3[] changed={new(0,0,0),new(1,0,0),new(0,1,0),new(-1,0,.3f)};
        SkinSurfaceShading.Part Part(Vector3[] before,Vector3[] after,int[] triangles)=>new()
        {Original=before,Deformed=after,Normals=Enumerable.Repeat(Vector3.UnitZ,before.Length).ToArray(),
            Tangents=Enumerable.Repeat(new Vector4(1,0,0,-1),before.Length).ToArray(),Triangles=triangles};
        var triangles=new[]{0,1,2,0,2,3};
        var first=SkinSurfaceShading.Apply(new[]{Part(points,changed,triangles)});
        var stretched=(Vector3[])changed.Clone();stretched[3]*=9;
        var second=SkinSurfaceShading.Apply(new[]{Part(points,stretched,triangles)});
        check("Fixed rest-corner weighting rejects shading bias from changing face area",
            Vector3.Distance(first.Parts[0].Normals[0],second.Parts[0].Normals[0])<1e-5f);

        const int nx=7,ny=25;
        var plane=new Vector3[nx*ny];var rippled=new Vector3[plane.Length];var index=new List<int>();
        for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)
        {int i=y*nx+x;plane[i]=new(x,y,0);rippled[i]=new(x,y,.12f*MathF.Sin(y*MathF.PI*.5f));
            if(x<nx-1 && y<ny-1)index.AddRange(new[]{i,i+1,i+nx,i+1,i+nx+1,i+nx});}
        var part=Part(plane,rippled,index.ToArray());
        var rough=SkinSurfaceShading.Apply(new[]{part},0);var smooth=SkinSurfaceShading.Apply(new[]{part},.65f);
        float Bands(Vector3[] n){float value=0;for(int y=2;y<ny-3;y++)value+=Vector3.DistanceSquared(n[y*nx+3],n[(y+1)*nx+3]);return value;}
        check("Orientation smoothing reduces horizontal lighting bands",Bands(smooth.Parts[0].Normals)<Bands(rough.Parts[0].Normals)*.6f);
        check("Lighting smoothing never changes vertex positions",rough.Parts[0].Vertices.SequenceEqual(smooth.Parts[0].Vertices));
        var prior=PriorSurfaceShading.Apply(new[]{new PriorSurfaceShading.Part{Original=part.Original,Deformed=part.Deformed,Normals=part.Normals,Tangents=part.Tangents,Triangles=part.Triangles}});
        check("New lighting path has exactly the same vertices as 0.1.12",prior.Parts[0].Vertices.SequenceEqual(smooth.Parts[0].Vertices));
        part.DetailProtection=Enumerable.Repeat(1f,plane.Length).ToArray();
        var protectedPatch=SkinSurfaceShading.Apply(new[]{part},1);
        check("Navel detail protection prevents smoothing away eversion",protectedPatch.Parts[0].Normals.SequenceEqual(rough.Parts[0].Normals));
        check("Lighting tangent basis remains unit, perpendicular and keeps handedness",smooth.Parts[0].Normals.Select((n,i)=>
            MathF.Abs(n.Length()-1)<1e-5f && MathF.Abs(Vector3.Dot(n,new Vector3(smooth.Parts[0].Tangents[i].X,smooth.Parts[0].Tangents[i].Y,smooth.Parts[0].Tangents[i].Z)))<1e-5f && smooth.Parts[0].Tangents[i].W==-1).All(x=>x));
        var p=new VtxSettings { NavelPreviewFull=false };
        check("Default 0.7-stage navel attenuation is observable",MathF.Abs(BellyShape.NavelStageResponse(.7f,p)-.1035156f)<1e-5f);
        p.NavelPreviewFull=true;
        check("Full navel preview bypasses only stage attenuation",BellyShape.NavelStageResponse(.7f,p)==1 && BellyShape.NavelStageResponse(0,p)==0);
        var box=new SkinBounds.Box();box.Include(new Vector3(-2,-3,-4));box.Include(new Vector3(4,5,6));
        var map=Matrix4x4.CreateScale(2,.3f,-4)*Matrix4x4.CreateRotationX(.8f)*Matrix4x4.CreateRotationY(.4f)*Matrix4x4.CreateTranslation(173,-5,121);
        var transformed=box.Transform(map);
        check("Bounds handle rotation, nonuniform and negative scale in world space",
            new[]{-2f,4}.All(x=>new[]{-3f,5}.All(y=>new[]{-4f,6}.All(z=>transformed.Contains(Vector3.Transform(new(x,y,z),map))))));
    }

    internal static void Capture(string input,Action<string,bool> check)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(input));var root=doc.RootElement;
        var origin=V(root.GetProperty("frame").GetProperty("center"));
        var anatomy=root.GetProperty("anatomy");
        var bodies=root.GetProperty("meshes").EnumerateArray().Where(m=>m.GetProperty("body").GetBoolean() && m.GetProperty("active").GetBoolean() && !m.GetProperty("mesh").GetString()!.Contains("armleg")).ToArray();
        var all=bodies.SelectMany(m=>Points(m,"vertices")).Select(v=>v-origin).ToArray();
        var profile=TorsoProfile.Build(all,anatomy.GetProperty("PelvicFloor").GetSingle(),0,anatomy.GetProperty("SampleMax").GetSingle());
        var settings=JsonSerializer.Deserialize<VtxSettings>(root.GetProperty("settings").GetRawText())!;
        float maximumOutside=0,maximumOld=0,maximumNavel=0;int patchCount=0;bool safePatch=true;
        foreach(float stage in new[]{0f,.33333334f,.55f,.7f,1f})
        foreach(var m in bodies)
        {
            var original=Points(m,"vertices");var influence=Floats(m,"influence");
            var breast=m.GetProperty("breastWeights").ValueKind==JsonValueKind.Null?new float[original.Length]:Floats(m,"breastWeights");
            float Factor(int i)=>influence[i]*(1-BellyShape.BreastRestore(breast[i],settings.BreastGuardStrength));
            var before=new Vector3[original.Length];var after=new Vector3[original.Length];
            settings.NavelPreviewFull=false;
            for(int i=0;i<original.Length;i++)
            {
                var v=original[i]-origin;
                maximumOld=MathF.Max(maximumOld,Vector3.Distance(PriorNavelShape.Deform(v,profile,stage,settings),BellyShape.Deform(v,profile,stage,settings)));
            }
            // The full-effect UI preset intentionally affects ONLY the old navel.
            settings.NavelPreviewFull=true;settings.NavelEversion=1;settings.NavelHeight=.012f;settings.NavelRadius=.06f;settings.NavelProportion=.85f;
            var off=JsonSerializer.Deserialize<VtxSettings>(JsonSerializer.Serialize(settings))!;off.NavelEversion=off.NavelProportion=0;
            for(int i=0;i<original.Length;i++)
            {
                var v=original[i]-origin;before[i]=v+(BellyShape.Deform(v,profile,stage,off)-v)*Factor(i);
                after[i]=v+(BellyShape.Deform(v,profile,stage,settings)-v)*Factor(i);
                float d=Vector3.Distance(before[i],after[i]);float radius=settings.NavelRadius*profile.Span;
                float r=MathF.Sqrt(v.X*v.X+(v.Y-profile.SkinNavelY)*(v.Y-profile.SkinNavelY)/1.69f)/radius;
                if(r>=1 || v.Z<=profile.AxisAt(v.Y))maximumOutside=MathF.Max(maximumOutside,d);
                if(stage==.7f && d>1e-6f){patchCount++;maximumNavel=MathF.Max(maximumNavel,d);}
            }
            var tris=m.GetProperty("triangles").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            for(int t=0;t<tris.Length;t+=3)
            {
                int a=tris[t],b=tris[t+1],c=tris[t+2];
                if(before[a]==after[a] && before[b]==after[b] && before[c]==after[c])continue;
                var n0=Vector3.Cross(before[b]-before[a],before[c]-before[a]);var n1=Vector3.Cross(after[b]-after[a],after[c]-after[a]);
                if(n0.LengthSquared()>1e-12f)safePatch &= n0.Z*n1.Z>0 && n1.Length()>.05f*n0.Length();
            }
        }
        check("Captured body: old navel mode matches 0.1.12 exactly at five stages",maximumOld==0);
        check("Captured body: full-effect preset moves zero vertices outside navel patch",maximumOutside==0);
        check("Captured body: navel preset visibly changes original patch at stage 0.7",patchCount>5 && maximumNavel>.02f);
        check("Captured body: navel preset does not reverse or collapse local material triangles",safePatch);
        Console.WriteLine($"RENDER FIX NAVEL: stage0.7 changed={patchCount}, maxDelta={maximumNavel:R}, outsideDelta={maximumOutside:R}, oldModeDelta={maximumOld:R}");

        foreach(var m in root.GetProperty("meshes").EnumerateArray().Where(m=>m.GetProperty("active").GetBoolean() && m.GetProperty("virtualBoneIndex").GetInt32()>0))
        {
            var positions=Points(m,"deformed");var alpha=Floats(m,"virtualBlend");
            var weights=m.GetProperty("originalSkinWeights").EnumerateArray().Select(row=>Enumerable.Range(0,4).Select(k=>new VirtualWeights.Influence(row[k*2].GetInt32(),row[k*2+1].GetSingle())).ToArray()).ToArray();
            int n=m.GetProperty("virtualBoneIndex").GetInt32();
            var native=m.GetProperty("skinToWorldRowMajor").EnumerateArray().Take(n).Select(Matrix).ToArray();
            var toRef=Matrix(m.GetProperty("toReferenceRowMajor"));var virtualMatrix=toRef*Matrix(root.GetProperty("virtualAxis").GetProperty("transform"));
            var envelope=SkinBounds.Build(positions,weights,alpha,n);bool contained=true;int cases=0;
            foreach(float angle in new[]{0f,.8f,1.5f,3.14f})
            {
                var pose=native.Select((mat,i)=>mat*Matrix4x4.CreateRotationX(angle*(i%4)/3)*Matrix4x4.CreateTranslation(137,21,-35)).ToArray();
                var vm=virtualMatrix*Matrix4x4.CreateRotationX(-angle*.7f)*Matrix4x4.CreateTranslation(137,21,-35);
                var box=envelope.Evaluate(i=>pose[i],vm);
                for(int i=0;i<positions.Length;i++)
                {
                    var skin=Vector3.Zero;foreach(var w in weights[i])if(w.Weight>0)skin+=Vector3.Transform(positions[i],pose[w.Bone])*w.Weight;
                    var exact=Vector3.Lerp(skin,Vector3.Transform(positions[i],vm),alpha[i]);
                    contained &= box.Contains(exact,1e-4f);cases++;
                }
            }
            check("Captured "+m.GetProperty("renderer").GetString()+": world bounds contain all native/virtual vertices across four poses",contained);
            Console.WriteLine($"RENDER FIX BOUNDS: {m.GetProperty("renderer").GetString()} checked={cases}, sourceBones={n}");
        }
    }
    private static Vector3 V(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle());
    private static Vector3[] Points(JsonElement m,string k)=>m.GetProperty(k).EnumerateArray().Select(V).ToArray();
    private static float[] Floats(JsonElement m,string k)=>m.GetProperty(k).EnumerateArray().Select(x=>x.GetSingle()).ToArray();
    private static Matrix4x4 Matrix(JsonElement e){var a=e.EnumerateArray().Select(x=>x.GetSingle()).ToArray();return new(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);}
}
