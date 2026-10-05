using ALPregnancy;
using System.Numerics;
using System.Text.Json;

internal static class ShapeRigRegression
{
    internal static void Run(Action<string, bool> check)
    {
        var p = new VtxSettings();
        var torso = new TorsoProfile
        {
            PelvicFloor=9.23f, Pubis=9.45f, Navel=10.685f, Ribs=12.19f, SampleMin=9.23f, SampleMax=12.52f,
            Front=Enumerable.Repeat(.7f,21).ToArray(),Back=Enumerable.Repeat(-1f,21).ToArray(),Width=Enumerable.Repeat(1.4f,21).ToArray()
        };
        var sample=new Vector3(0,torso.Navel,.7f);
        check("Zero growth is exact identity",BellyShape.Deform(sample,torso,0,p)==sample);
        var moved=BellyShape.Deform(sample,torso,1,p);
        check("Default sag moves the front abdomen down and forward",moved.Y<sample.Y-.1f && moved.Z>sample.Z+.5f);
        var noSag=new VtxSettings{SagStrength=0};
        check("Sag zero preserves exact skin height",BellyShape.Deform(sample,torso,1,noSag).Y==sample.Y);
        var late=BellyShape.Growth(torso,1,p);
        var unlifted=BellyShape.Growth(torso,1,new VtxSettings{LowerPoleLift=0});
        check("Lower-pole lift keeps the top fixed",late.Bottom>unlifted.Bottom && Math.Abs(late.Top-unlifted.Top)<1e-5f);
        var mid=BellyShape.Growth(torso,4f/9,p);
        var large=BellyShape.Growth(torso,4f/9,new VtxSettings{MidVolume=1.4f});
        check("Mid-volume control increases the second stage",large.Depth>mid.Depth && large.HalfWidth>mid.HalfWidth);
        var high=new Vector3(0,torso.Navel+.6f,.7f);
        float broad=BellyShape.Deform(high,torso,4f/9,p).Z-high.Z;
        float local=BellyShape.Deform(high,torso,4f/9,new VtxSettings{WallSmoothing=0}).Z-high.Z;
        check("Early smoothing reaches the future upper-abdomen domain",broad>local+.003f);
        var flank=new Vector3(.9f,torso.Navel,.5f);
        var thin=BellyShape.Deform(flank,torso,1,new VtxSettings{GrowthWidth=.5f});
        var wide=BellyShape.Deform(flank,torso,1,new VtxSettings{GrowthWidth=2});
        check("Width changes transverse expansion",wide.X>thin.X+.05f);
        var back=new Vector3(0,torso.Navel,-1);
        check("Back skin remains fixed",BellyShape.Deform(back,torso,1,p)==back);
        var below=new Vector3(0,8,.3f);
        check("Skin below the complete membrane domain remains fixed",BellyShape.Deform(below,torso,1,p)==below);
        var dimple=sample-new Vector3(0,0,.035f);
        check("Midline navel detail survives displacement",MathF.Abs((BellyShape.Deform(sample,torso,1,p).Z-BellyShape.Deform(dimple,torso,1,p).Z)-.035f)<1e-5f);
        int buildCount=BellyShape.FieldBuildCount;
        for(int i=0;i<10000;i++) BellyShape.Deform(sample,torso,1,p);
        check("Repeated vertex queries do not rebuild the field",BellyShape.FieldBuildCount==buildCount);
        p.SagStrength=.5f;BellyShape.Deform(sample,torso,1,p);
        check("Changing sag invalidates the field",BellyShape.FieldBuildCount==buildCount+1);
        bool ordered=true;
        var strong=new VtxSettings{SagStrength=2};
        float prior=float.NegativeInfinity;
        for(float y=8.5f;y<13.5f;y+=.01f)
        {
            var v=BellyShape.Deform(new Vector3(0,y,.7f),torso,1,strong);
            ordered &= v.Y>prior && float.IsFinite(v.X+v.Y+v.Z);prior=v.Y;
        }
        check("Maximum sag preserves vertical row ordering",ordered);
        check("Pure breast guard remains fixed",BellyShape.BreastRestore(1,1)==1);
        check("No waist weighting leaves legs unchanged",BellyShape.BoneInfluence(0,1)==0);
        check("Torso weights retain full influence",BellyShape.BoneInfluence(1,0)==1);
        bool sliderFinite=true;
        Action<VtxSettings,float>[] controls={
            (v,x)=>v.WallSmoothing=x*2,(v,x)=>v.SagStrength=x*2,
            (v,x)=>v.MidVolume=.75f+x*.75f,(v,x)=>v.LowerPoleLift=x*2,
            (v,x)=>v.SkinClearance=x*.2f,
            (v,x)=>v.GrowthWidth=.5f+x*1.5f,(v,x)=>v.VerticalRange=.6f+x*.6f};
        foreach(var control in controls)
        foreach(float value in new[]{0f,1f})
        {
            var setting=new VtxSettings();control(setting,value);
            foreach(float stage in new[]{4f/9,1f})
            for(float y=8.8f;y<12.8f;y+=.13f)
            foreach(float x in new[]{0f,.4f,.9f})
            {
                var v=new Vector3(x,y,.7f);var o=BellyShape.Deform(v,torso,stage,setting);
                sliderFinite &= float.IsFinite(o.X+o.Y+o.Z) && o.Y<=v.Y+1e-6f && o.Z>=v.Z-1e-6f;
            }
        }
        check("Every new slider endpoint produces finite outward/downward geometry",sliderFinite);
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var timed=new VtxSettings{WallSmoothing=.9876f};BellyShape.Deform(sample,torso,1,timed);watch.Stop();
        Console.WriteLine($"PERFORMANCE: one field rebuild {watch.Elapsed.TotalMilliseconds:F2} ms; cached sampling reuses this field.");
        Vector3 offset = new(.15f,.05f,.2f);
        check("Unchanged surface preserves full garment gap exactly", SurfaceAttachment.RotateOffset(offset,Vector3.UnitZ,Vector3.UnitZ) == offset);
        Vector3 tilted = SurfaceAttachment.RotateOffset(offset,Vector3.UnitZ,Vector3.UnitY);
        check("Rotated surface preserves garment gap length including tangential component", MathF.Abs(tilted.Length()-offset.Length()) < 1e-6f && MathF.Abs(tilted.Y-offset.Z) < 1e-6f);

        string[] sourceNames = { "cf_c_spine_waist01", "cf_c_spine_waist02", "cf_c_spine_01", "cf_c_spine_siri_L", "cf_c_spine_siri_R" };
        string[] targetNames = { "cf_s_waist01", "cf_s_waist02", "cf_s_spine01", "cf_s_siri_L", "cf_s_siri_R" };
        Vector3[] points = { new(0,1,0), new(0,1.2f,0), new(0,1.3f,.1f), new(-.2f,.9f,-.1f), new(.2f,.9f,-.1f) };
        Matrix4x4 expected = Matrix4x4.CreateScale(10) * Matrix4x4.CreateRotationX(.8f) * Matrix4x4.CreateRotationY(-.3f) * Matrix4x4.CreateTranslation(2,4,-1);
        Matrix4x4[] Poses(Vector3[] positions, bool otherAxes) => positions.Select((v,i) =>
        {
            Matrix4x4.Invert(Matrix4x4.CreateRotationZ(otherAxes ? .2f*(i+1) : 0) * Matrix4x4.CreateTranslation(v),out var inv); return inv;
        }).ToArray();
        var sourcePoses = Poses(points,false);
        var targetPoints = points.Select(v => Vector3.Transform(v,expected)).ToArray();
        var targetPoses = Poses(targetPoints,true);
        check("Native clothing names map to supplied skin names", sourceNames.Select(RigBoneNames.Canonical).SequenceEqual(targetNames));
        bool mapped = RestSpaceMapping.TryCreateAliased(sourceNames,sourcePoses,targetNames,targetPoses,out var map,out var inverse,out int count,out float error);
        check("Anatomical origin fit supports different bone axes and mesh scales", mapped && count == 5 && error < 1e-4f);
        check("Fitted clothing matrix preserves a nonsampled point", Vector3.Distance(Vector3.Transform(sample,map),Vector3.Transform(sample,expected)) < 1e-4f);
        check("Clothing coordinates round trip through the fitted inverse", Vector3.Distance(Vector3.Transform(Vector3.Transform(sample,map),inverse),sample) < 1e-5f);
        check("Two named bones cannot invent a coordinate frame", !RestSpaceMapping.TryCreateAliased(sourceNames[..2],sourcePoses[..2],targetNames,targetPoses,out _,out _,out _,out _));
        check("Collinear points reject ambiguous rotation", !RestPoseFit.TryFit(new[] { Vector3.Zero, Vector3.UnitY, Vector3.UnitY*2 },new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitX*2 },out _,out _));
        var badPoints = targetPoints.ToArray(); badPoints[1] += new Vector3(4,0,0);
        check("Wrong anatomical correspondence fails residual validation", !RestPoseFit.TryFit(points,badPoints,out _,out _));
        check("Reflections cannot silently swap the rig handedness", !RestPoseFit.TryFit(points,points.Select(v => v * new Vector3(-1,1,1)).ToArray(),out _,out _));
        var largeOffset = targetPoints.Select(v => v+new Vector3(100,200,300)).ToArray();
        check("Mapping tolerances do not depend on mesh origin distance", RestPoseFit.TryFit(points,largeOffset,out _,out float translatedError) && translatedError < .001f);

        var fixture = JsonSerializer.Deserialize<BoneTrace[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"user-trace-0.1.3-bones.json")))!;
        var top = fixture.First(x => x.Mesh == "o_cf_top_amicardigan00_a");
        check("Captured cardigan has at least ten anatomical body correspondences", top.Source.Select(RigBoneNames.Canonical).Intersect(top.Target).Count() >= 10);
        check("All 20 clothing pieces across both captured characters have a name route through the native full rig", fixture.Length == 20 && fixture.All(x => x.Source.Intersect(top.Source).Count() >= 3 || x.Source.Select(RigBoneNames.Canonical).Intersect(x.Target).Count() >= 3));
        check("Auxiliary skirt bones are never renamed into anatomy bones", RigBoneNames.Canonical("cf_j_sk_02_00") == "cf_j_sk_02_00");

        // A sparse skirt gets an exact native-rig map, then the donor-to-skin fit.
        string[] skirtBones = { "cf_j_sk_00_00", "cf_j_sk_00_01", "cf_j_sk_00_02" };
        var skirtRest = Matrix4x4.CreateRotationZ(.5f) * Matrix4x4.CreateTranslation(1,2,3);
        var donorPoses = sourcePoses[..3];
        var skirtPoses = donorPoses.Select(bp => skirtRest*bp).ToArray();
        bool bridged = RestSpaceMapping.TryCreate(skirtBones,skirtPoses,skirtBones,donorPoses,out var toDonor,out _,out _,out _);
        check("Sparse skirt can compose native-rig and body conversions", bridged && Vector3.Distance(Vector3.Transform(sample,toDonor*map),Vector3.Transform(Vector3.Transform(sample,skirtRest),expected)) < 1e-4f);
    }

    private sealed class BoneTrace
    {
        public string Mesh { get; set; } = "";
        public string[] Source { get; set; } = Array.Empty<string>();
        public string[] Target { get; set; } = Array.Empty<string>();
    }
}

