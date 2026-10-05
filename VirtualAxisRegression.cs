using ALPregnancy;
using System.Numerics;

internal static class VirtualAxisRegression
{
    internal static void Run(Action<string,bool> check)
    {
        var p=new VtxSettings();var axis=new VirtualAxisMath.Axis(new(0,9.8f,.2f),new(0,11.4f,.7f));
        var pelvis=Matrix4x4.CreateScale(.8f,1.1f,.9f)*Matrix4x4.CreateRotationY(.3f)*Matrix4x4.CreateTranslation(2,3,4);
        var spine=Matrix4x4.CreateTranslation(0,-10.7f,0)*Matrix4x4.CreateRotationX(.9f)*Matrix4x4.CreateTranslation(0,10.7f,0)*pelvis;
        var result=VirtualAxisMath.Evaluate(axis,pelvis,spine,p);
        check("Virtual axis fixes the initial-volume anchor to the pelvis",Vector3.Distance(Vector3.Transform(axis.Anchor,result.Transform),Vector3.Transform(axis.Anchor,pelvis))<1e-5f);
        check("Virtual axis preserves pelvis-relative volume",MathF.Abs(result.Transform.GetDeterminant()/pelvis.GetDeterminant()-1)<1e-5f);
        var freeLength=Vector3.TransformNormal(axis.Upper-axis.Anchor,pelvis).Length();
        check("Direction pull never stretches the axis to the spine distance",MathF.Abs(Vector3.TransformNormal(axis.Upper-axis.Anchor,result.Transform).Length()-freeLength)<1e-5f);
        float residual=MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(Vector3.TransformNormal(axis.Upper-axis.Anchor,result.Transform)),result.TargetAxis),-1,1))*180/MathF.PI;
        check("Angular response applies the requested fraction of the angle",MathF.Abs(residual-result.AngleDegrees*(1-result.Pull))<.02f);
        var turn=Matrix4x4.CreateRotationZ(1.5f)*Matrix4x4.CreateRotationX(.3f)*Matrix4x4.CreateTranslation(4,-6,2);
        var turned=VirtualAxisMath.Evaluate(axis,pelvis*turn,spine*turn,p);
        var point=new Vector3(.6f,10.9f,2);
        check("Lying/global rotation commutes with the virtual equation",Vector3.Distance(Vector3.Transform(point,turned.Transform),Vector3.Transform(Vector3.Transform(point,result.Transform),turn))<2e-5f);
        var common=VirtualAxisMath.Evaluate(axis,pelvis,pelvis,p);
        check("Common rigid motion produces no artificial bend",Vector3.Distance(Vector3.Transform(point,common.Transform),Vector3.Transform(point,pelvis))<1e-5f);
        var carrier=Matrix4x4.CreateRotationX(.7f)*Matrix4x4.CreateTranslation(2,3,4);
        var map=Matrix4x4.CreateScale(1.3f)*Matrix4x4.CreateRotationZ(.2f);
        bool ok=VirtualAxisMath.PaletteBind(map,result.Transform,carrier,out var bind);
        check("Virtual palette equals the direct vertex equation",ok && Vector3.Distance(Vector3.Transform(point,bind*carrier),Vector3.Transform(point,map*result.Transform))<1e-5f);
        check("Singular carriers are rejected without corrupting a mesh",!VirtualAxisMath.PaletteBind(map,result.Transform,default,out _));
        float prior=-1;bool monotone=true;
        for(int i=0;i<100;i++){float w=VirtualAxisMath.Weight(i*.01f,2.7f,p);monotone&=w>=prior && w>=0 && w<=1;prior=w;}
        check("Displacement mixing is bounded and monotone",monotone);
        check("Unmoved skin keeps its original equation",VirtualAxisMath.Weight(0,2.7f,p)==0);
        check("Strongly expanded skin reaches the virtual equation",VirtualAxisMath.Weight(2,2.7f,p)==1);
        var native=new[]{new VirtualWeights.Influence(0,.45f),new VirtualWeights.Influence(1,.35f),new VirtualWeights.Influence(2,.19f),new VirtualWeights.Influence(3,.01f)};
        var recipes=new List<VirtualWeights.Recipe>();int Slot(VirtualWeights.Recipe r){int i=recipes.IndexOf(r);if(i<0){i=recipes.Count;recipes.Add(r);}return i+4;}
        check("Disabled weight mixing retains every original influence",VirtualWeights.Blend(native,0,Slot).SequenceEqual(native));
        var full=VirtualWeights.Blend(native,1,Slot);
        check("Full virtual mixing uses only the virtual matrix",full.Length==1 && full[0].Bone==4 && full[0].Weight==1);
        bool weights=true;float error=0;var matrices=new[]{Matrix4x4.Identity,pelvis,spine,turn};
        for(int i=0;i<=100;i++)
        {
            float alpha=i/100f;var a=VirtualWeights.Blend(native,alpha,Slot);
            weights &= a.Length<=4 && a.All(x=>x.Weight>=0) && MathF.Abs(a.Sum(x=>x.Weight)-1)<1e-6f;
            Matrix4x4 Transform(int k){if(k<4)return matrices[k];var r=recipes[k-4];return r.Bone<0?result.Transform:Matrix4x4.Lerp(matrices[r.Bone],result.Transform,r.VirtualShare);}
            var got=a.Aggregate(Vector3.Zero,(s,k)=>s+Vector3.Transform(point,Transform(k.Bone))*k.Weight);
            var old=native.Aggregate(Vector3.Zero,(s,k)=>s+Vector3.Transform(point,matrices[k.Bone])*k.Weight);
            var expected=Vector3.Lerp(old,Vector3.Transform(point,result.Transform),alpha);
            error=MathF.Max(error,Vector3.Distance(got,expected));
        }
        check("Four-influence fusion preserves all five mathematical influences",weights && error<1e-5f);
        var material=new MaterialBlendField(2.7f);material.Add(Vector3.Zero,.2f);material.Add(new Vector3(.1f,0,0),.8f);
        check("Coincident clothing and skin have exactly the same blend",material.Sample(Vector3.Zero,1)==.2f && material.Sample(new Vector3(.1f,0,0),0)==.8f);
        check("Nearby clothing interpolates a bounded body blend",MathF.Abs(material.Sample(new Vector3(.05f,0,.01f),0)-.5f)<1e-5f);
        check("Clothing outside the body neighborhood retains its own blend",material.Sample(new Vector3(10,10,10),.4f)==.4f);
        var noPull=new VtxSettings {AxisPullLow=0,AxisPullHigh=0};
        check("Zero axis pull retains the pelvis-driven free axis",Vector3.Distance(Vector3.Transform(point,VirtualAxisMath.Evaluate(axis,pelvis,spine,noPull).Transform),Vector3.Transform(point,pelvis))<1e-5f);
        var fullPull=new VtxSettings {AxisPullLow=1,AxisPullHigh=1};
        var followed=VirtualAxisMath.Evaluate(axis,pelvis,spine,fullPull);
        check("Full axis pull aligns direction without fixing upper position",Vector3.Dot(Vector3.Normalize(Vector3.TransformNormal(axis.Upper-axis.Anchor,followed.Transform)),followed.TargetAxis)>.99999f);
        var reverseAxis=new VirtualAxisMath.Axis(Vector3.Zero,Vector3.UnitY);
        var reverse=VirtualAxisMath.Evaluate(reverseAxis,Matrix4x4.Identity,Matrix4x4.CreateRotationX(MathF.PI),p);
        check("Antiparallel axes produce a finite anchored transform",VirtualAxisMath.Finite(reverse.Transform) && Vector3.Transform(Vector3.Zero,reverse.Transform).Length()<1e-6f);
        var watch=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<10000;i++)VirtualAxisMath.Evaluate(axis,pelvis,spine,p);
        Console.WriteLine($"VIRTUAL AXIS MATH: {watch.Elapsed.TotalMilliseconds/10000:F6} ms/evaluation; excludes Unity palette setter.");
    }
}
