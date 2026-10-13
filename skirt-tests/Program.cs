using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using ALPregnancy;

internal static class Program
{
    static int assertions;
    internal static void Check(bool ok,string message) { assertions++;if(!ok)throw new Exception(message); }
    internal static void Near(Vector3 a,Vector3 b,string message,float tolerance=1e-5f)=>Check(Vector3.Distance(a,b)<tolerance,$"{message}: {a} != {b}");
    internal static TorsoProfile Torso=new() {Pubis=-.4f,Ribs=.6f,Navel=0,PelvicFloor=-.5f,SampleMin=-.5f,SampleMax=.7f,
        Front=new[]{.2f,.2f},Back=new[]{-.2f,-.2f},Width=new[]{.3f,.3f}};
    internal sealed record Data(Vector3[] Original,Vector3[] Deformed,VirtualWeights.Influence[][] Weights,
        bool[] Excluded,bool[] Controlled,int[] Parents,Vector3[] Roots,int[] Triangles);
    internal static Data Garment(int columns,int rows)
    {
        int count=columns*rows;
        var original=new Vector3[count];var deformed=new Vector3[count];
        var weights=new VirtualWeights.Influence[count][];var excluded=new bool[count];
        var controlled=new bool[1+columns*3];var parents=new int[controlled.Length];Array.Fill(parents,-1);
        var roots=new Vector3[controlled.Length];var triangles=new List<int>();
        for(int c=0;c<columns;c++)
        {
            float x=(c/(float)(columns-1)-.5f)*.5f;
            for(int b=0;b<3;b++)
            {int i=1+c*3+b;controlled[i]=true;parents[i]=b==0?-1:i-1;roots[i]=new Vector3(x,-b*.35f,.25f);}
            for(int row=0;row<rows;row++)
            {
                int i=c*rows+row;float y=.3f-row/(float)(rows-1)*1.4f;
                original[i]=new Vector3(x,y,.25f);
                float delta=.2f*MathF.Exp(-MathF.Pow((y-.08f)/.22f,2));
                deformed[i]=original[i]+new Vector3(0,0,delta);
                float ownership=Math.Clamp((.2f-y)/.3f,0,1);
                int bone=1+c*3+(y<-.65f?2:y<-.25f?1:0);
                weights[i]=new[]{new VirtualWeights.Influence(bone,ownership),new VirtualWeights.Influence(0,1-ownership)};
                if(c>0&&row>0) {int a=i-rows-1,b=i-rows,d=i-1;triangles.AddRange(new[]{a,b,i,a,i,d});}
            }
        }
        return new(original,deformed,weights,excluded,controlled,parents,roots,triangles.ToArray());
    }
    static SkirtSkinning.Plan Prepare(Data d,Matrix4x4? toReference=null,Matrix4x4? inverseFrame=null)=>
        SkirtSkinning.Prepare(d.Original,d.Deformed,d.Weights,d.Excluded,d.Controlled,d.Parents,d.Roots,d.Triangles,
            toReference??Matrix4x4.Identity,inverseFrame??Matrix4x4.Identity,Torso);
    static void Main(string[] args)
    {
        Check(SkirtSkinning.IsBone("cf_j_sk_00_00")&&SkirtSkinning.IsBone("cf_j_sk_07_05"),"AL native bone names");
        foreach(string n in new[]{"cf_j_sk_00_00_end","cf_j_sk_0_00","cf_s_waist01","skirt","cf_j_sk_XX_00"})Check(!SkirtSkinning.IsBone(n),"non skirt excluded");
        SkirtDrape.Resolve(new[]{2,3,-1,0},new[]{new Vector3(0,0,.2f),new Vector3(0,0,.05f),new Vector3(0,0,.1f),new Vector3(0,0,.15f)},
            new[]{true,true,true,true},.001f,out var offsets,out var free);
        Check(!free[2]&&free[0]&&free[3]&&free[1],"shuffled hierarchy releases at last supported bone");
        Near(offsets[1],new(0,0,.2f),"carry does not accumulate down chain");
        SkirtDrape.Resolve(new[]{-1,0,1,-1,3},new[]{new Vector3(0,0,.1f),new Vector3(0,0,.0999f),Vector3.Zero,new Vector3(0,0,.3f),new Vector3(0,0,.4f)},
            new[]{true,true,true,true,true},.001f,out offsets,out free);
        Check(!free[0]&&free[1]&&free[2]&&!free[3]&&!free[4],"tolerance and independent chains");
        bool cyclic=false;try{SkirtDrape.Resolve(new[]{1,0},new Vector3[2],new[]{true,true},0,out _,out _);}catch(ArgumentException){cyclic=true;}Check(cyclic,"cycle fails safely");
        var d=Garment(12,20);var plan=Prepare(d);Check(plan!=null,"drape prepared");
        for(int i=0;i<d.Original.Length;i++)
        {
            float f=d.Weights[i].Select((w,k)=>w.Weight*plan.Free[i][k]).Sum();
            if(f>.99999f)Near(plan.Residual[i],d.Original[i],"fully free residual stays original");
        }
        // Change imported mesh coordinates and torso/world orientation. A T-pose
        // or asset axis change must not alter the physical drape solution.
        var meshMap=Matrix4x4.CreateRotationX(1.2f)*Matrix4x4.CreateTranslation(1,-2,.8f);
        Matrix4x4.Invert(meshMap,out var invMesh);
        var imported=d with {Original=Array.ConvertAll(d.Original,p=>Vector3.Transform(p,invMesh)),
            Deformed=Array.ConvertAll(d.Deformed,p=>Vector3.Transform(p,invMesh)),Roots=Array.ConvertAll(d.Roots,p=>Vector3.Transform(p,invMesh))};
        var importedPlan=Prepare(imported,meshMap);
        for(int i=0;i<d.Original.Length;i++)Near(Vector3.Transform(importedPlan.Visual[i],meshMap),plan.Visual[i],"imported bind axes invariant",3e-5f);
        Check(Prepare(d with {Deformed=d.Original})==null,"zero displacement has no skirt plan");
        Check(Prepare(d with {Controlled=new bool[d.Controlled.Length]})==null,"no skirt bones is a no-op");
        var sparse=d with {Weights=d.Weights.Select(ws=>ws.Select(w=>new VirtualWeights.Influence(w.Bone==0?0:1,w.Weight)).ToArray()).ToArray(),
            Controlled=new[]{false,true},Parents=new[]{-1,-1},Roots=new[]{Vector3.Zero,new Vector3(0,0,.25f)}};
        var sparsePlan=Prepare(sparse);
        Check(sparsePlan!=null&&!sparsePlan.Released.Any(x=>x)&&sparsePlan.Free.Any(ws=>ws.Any(x=>x>.99f)),"single long bone releases fabric below crest without a child joint");
        var protectedOne=d with {Excluded=(bool[])d.Excluded.Clone()};protectedOne.Excluded[19]=true;
        var protectedPlan=Prepare(protectedOne);Near(protectedPlan.Residual[19],d.Original[19],"excluded skirt vertex has no residual");
        Check(protectedPlan.Free[19].All(x=>x==0),"excluded skirt vertex has no free influence");
        var protectedData=d with {Excluded=Enumerable.Repeat(true,d.Original.Length).ToArray()};Check(Prepare(protectedData)==null,"excluded cloth cannot seed support");
        // Surface integration: subdividing one triangle must not increase its mass.
        var area=SkirtSkinning.SurfaceMeasure(new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitY},new[]{0,1,2});
        Check(MathF.Abs(area.Sum()-.5f)<1e-7f,"triangle measure is area");
        var refinedArea=SkirtSkinning.SurfaceMeasure(new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitY,new Vector3(.5f,.5f,0)},new[]{0,1,3,0,3,2});
        Check(MathF.Abs(area.Sum()-refinedArea.Sum())<1e-7f,"tessellation preserves sampling mass");
        RandomPacking();
        BellyVertexMorph.RuntimeTests();
        var clock=Stopwatch.StartNew();var dense=Garment(200,500);clock.Restart();var densePlan=Prepare(dense);clock.Stop();
        Check(densePlan!=null&&densePlan.Visual.All(v=>float.IsFinite(v.LengthSquared())),"100k vertex drape finite");
        Console.WriteLine($"100000 vertices / {dense.Triangles.Length/3} triangles: prepare {clock.Elapsed.TotalMilliseconds:F1} ms (managed offline, not game timing)");
        if(args.Length>0)RealModels(args[0]);
        Console.WriteLine($"PASS: {assertions} skirt assertions (production kernels and production virtual runtime with Unity API doubles).");
    }
    static void RandomPacking()
    {
        var rng=new Random(421);
        for(int trial=0;trial<1000;trial++)
        {
            var native=new VirtualWeights.Influence[4];var free=new float[4];var matrices=new Matrix4x4[4];var supported=new Matrix4x4[4];var released=new Matrix4x4[4];
            float total=0;for(int k=0;k<4;k++){float w=(float)rng.NextDouble();native[k]=new(k,w);total+=w;free[k]=(float)rng.NextDouble();}
            for(int k=0;k<4;k++)native[k]=new(k,native[k].Weight/total);
            var virtualMatrix=Matrix4x4.CreateRotationY(.8f)*Matrix4x4.CreateTranslation(1,2,3);
            float alpha=trial%10==0?0:trial%10==1?1:(float)rng.NextDouble();
            for(int k=0;k<4;k++)
            {
                matrices[k]=Matrix4x4.CreateScale(1+.1f*k)*Matrix4x4.CreateRotationX(k*.4f);
                SkirtSkinning.Matrices(matrices[k],new(.1f,.2f,.3f),new(.1f,.2f,.3f),virtualMatrix,out supported[k],out released[k]);
                var test=Vector3.UnitX;
                Near(Vector3.TransformNormal(test,released[k]),Vector3.TransformNormal(test,matrices[k]),"free matrix linear part remains native");
            }
            var recipes=new List<SkirtSkinning.Recipe>();int Slot(SkirtSkinning.Recipe r){recipes.Add(r);return recipes.Count-1;}
            var packed=SkirtSkinning.Blend(native,alpha,free,Slot);Check(packed.Length<=4,"four slots exact packing");
            Check(MathF.Abs(packed.Sum(w=>w.Weight)-1)<1e-6f,"positive mass conserved");
            var p=new Vector3(.4f,-.3f,.7f);var expected=Vector3.Zero;
            for(int k=0;k<4;k++)expected+=Vector3.Transform(p,Matrix4x4.Lerp(Matrix4x4.Lerp(supported[k],virtualMatrix,alpha),released[k],free[k]))*native[k].Weight;
            var actual=Vector3.Zero;
            foreach(var w in packed)
            {
                var r=recipes[w.Bone];var m=r.Bone<0?virtualMatrix:Matrix4x4.Lerp(Matrix4x4.Lerp(supported[r.Bone],virtualMatrix,r.Alpha),released[r.Bone],r.Free);
                actual+=Vector3.Transform(p,m)*w.Weight;
            }
            Near(actual,expected,"packing agrees with unfactored skin equation");
        }
    }
    static void RealModels(string path) { RealReplay.Run(path,Check,Near); }
}
