using System.Numerics;
using System.Text.Json;
using System.Diagnostics;
using ALPregnancy;

// Optional local game assets extracted by work/extract_as_skirts.py. Game data
// is not part of the source/release archive.
internal static class RealReplay
{
    internal static void Run(string path, Action<bool,string> check, Action<Vector3,Vector3,string,float> near)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(path));
        Vector3 V(JsonElement v)=>new(v[0].GetSingle(),v[1].GetSingle(),v[2].GetSingle());
        Matrix4x4 M(JsonElement m)=>new(m[0].GetSingle(),m[1].GetSingle(),m[2].GetSingle(),m[3].GetSingle(),
            m[4].GetSingle(),m[5].GetSingle(),m[6].GetSingle(),m[7].GetSingle(),m[8].GetSingle(),m[9].GetSingle(),m[10].GetSingle(),m[11].GetSingle(),
            m[12].GetSingle(),m[13].GetSingle(),m[14].GetSingle(),m[15].GetSingle());
        int models=0,active=0;
        foreach(var mesh in doc.RootElement.GetProperty("meshes").EnumerateArray())
        {
            string name=mesh.GetProperty("name").GetString();
            var vertices=mesh.GetProperty("vertices").EnumerateArray().Select(V).ToArray();
            var names=mesh.GetProperty("names").EnumerateArray().Select(x=>x.GetString()).ToArray();
            var controlled=Array.ConvertAll(names,SkirtSkinning.IsBone);
            var parents=mesh.GetProperty("parents").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            for(int b=0;b<parents.Length;b++)if(!controlled[b]||parents[b]<0||!controlled[parents[b]])parents[b]=-1;
            var binds=mesh.GetProperty("bindposes").EnumerateArray().Select(M).ToArray();
            var rest=Array.ConvertAll(binds,m=>{Matrix4x4.Invert(m,out var r);return r;});
            var roots=Array.ConvertAll(rest,m=>m.Translation);
            var native=new VirtualWeights.Influence[vertices.Length][];
            var ws=mesh.GetProperty("weights");var ids=mesh.GetProperty("indices");
            for(int i=0;i<vertices.Length;i++)native[i]=Enumerable.Range(0,ws[i].GetArrayLength()).Select(k=>new VirtualWeights.Influence(ids[i][k].GetInt32(),ws[i][k].GetSingle())).ToArray();
            int waist=Array.FindIndex(names,n=>RigBoneNames.Canonical(n)=="cf_s_waist01");
            int spine=Array.FindIndex(names,n=>RigBoneNames.Canonical(n)=="cf_s_spine02");
            if(waist<0)continue;
            var center=roots[waist];
            // Use bind landmarks only: no live pose, Unity world orientation, or
            // assumed import scale. This is a skirt equation replay, not a full
            // character/body-anchor visual replay.
            float span=spine>=0?Vector3.Distance(roots[spine],center)*2:.4f;
            if(span<.01f)span=.4f;
            var points=Array.ConvertAll(vertices,p=>(p-center)/span);
            var rootPoints=Array.ConvertAll(roots,p=>(p-center)/span);
            // AL bind mesh is T-pose; find up from actual skeletal landmarks.
            var up=spine>=0?Vector3.Normalize(roots[spine]-center):Vector3.UnitY;
            var right=Vector3.UnitX;
            var forward=Vector3.Normalize(Vector3.Cross(right,up));right=Vector3.Normalize(Vector3.Cross(up,forward));
            Vector3 Local(Vector3 p)=>new(Vector3.Dot(p,right),Vector3.Dot(p,up),Vector3.Dot(p,forward));
            points=Array.ConvertAll(points,Local);rootPoints=Array.ConvertAll(rootPoints,Local);
            var deformed=Array.ConvertAll(points,p=>p+new Vector3(0,0,.25f*MathF.Exp(-MathF.Pow(p.Y/.24f,2))*Math.Clamp(p.Z/.15f,0,1)));
            var triangles=mesh.GetProperty("triangles").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            var clock=Stopwatch.StartNew();
            var plan=SkirtSkinning.Prepare(points,deformed,native,null,controlled,parents,rootPoints,triangles,Matrix4x4.Identity,Matrix4x4.Identity,Program.Torso);
            clock.Stop();models++;
            if(plan==null){Console.WriteLine($"real {name}: no supported skirt displacement");continue;}
            active++;
            int free=0;
            for(int i=0;i<points.Length;i++)
            {
                var carry=Vector3.Zero;float fraction=0;
                for(int k=0;k<native[i].Length;k++)
                {var w=native[i][k];carry+=plan.Offsets[w.Bone]*w.Weight*plan.Free[i][k];fraction+=w.Weight*plan.Free[i][k];}
                near(plan.Residual[i]+carry,plan.Visual[i],"real mesh rest compensation",1e-5f);
                if(fraction>.99999f){near(plan.Residual[i],points[i],"real free geometry",1e-5f);free++;}
                check(float.IsFinite(plan.Visual[i].LengthSquared()),"real finite point");
            }
            Console.WriteLine($"real {name}: vertices={points.Length} skirtBones={controlled.Count(x=>x)} released={plan.Released.Count(x=>x)} freeVertices={free} prepareMs={clock.Elapsed.TotalMilliseconds:F1}");
        }
        check(models>0&&active>0,"real AS skirt geometry replayed");
    }
}
