using ALPregnancy;
using System.Numerics;
using System.Text.Json;

internal static class UpperRestrictionsRegression
{
    internal static void Run(Action<string,bool> check)
    {
        var t = new TorsoProfile { PelvicFloor=-1.45f, Pubis=-1.23f, Navel=0, Ribs=1.5f,
            SampleMin=-1.6f, SampleMax=2, Front=new[]{.6f,.65f,.7f}, Back=new[]{-.4f,-.4f,-.4f}, Width=new[]{.8f,.9f,1f} };
        var p = JsonSerializer.Deserialize<VtxSettings>("{\"breastBoundaryRings\":99,\"breastBoundarySmoothing\":1}")!;
        check("Old presets default all additional restrictions to off", !p.BreastExclusionEnabled && !p.UpperBoneFilterEnabled && !p.UpperFieldFadeEnabled);
        check("Removed ring controls cannot survive serialization", !JsonSerializer.Serialize(p).Contains("breastBoundary"));
        var weights=new[]{0f,.01f,.9f};var welds=new[]{0,1,1};
        check("Disabled breast exclusion clears both owned vertices and welded duplicates", !BreastExclusion.Build(weights,3,welds,p.BreastExclusionEnabled).Any(x=>x));
        p.BreastExclusionEnabled=true;
        check("Reenabled breast exclusion retains original ownership semantics", BreastExclusion.Build(weights,3,welds,p.BreastExclusionEnabled).SequenceEqual(new[]{false,true,true}));
        foreach (float native in new[]{0f,.2f,1f})
        {
            check($"Upper filter off releases native weight {native}", BellyShape.DeformationInfluence(native,.1f,t,p)==1);
            check($"Lower native weight {native} stays unchanged", BellyShape.DeformationInfluence(native,-.1f,t,p)==native && BellyShape.DeformationInfluence(native,0,t,p)==native);
        }
        p.UpperBoneFilterEnabled=true;
        check("Upper bone filter can be restored", BellyShape.DeformationInfluence(.2f,.1f,t,p)==.2f);
        p.UpperBoneFilterEnabled=false;
        var lower=new Vector3(0,-.3f,t.FrontAt(-.3f));
        var before=BellyShape.Deform(lower,t,1,p);
        var growth=BellyShape.Growth(t,1,p);
        float top=growth.Top+.24f*t.Span;
        var upper=new Vector3(0,top-.06f*t.Span,t.FrontAt(top-.06f*t.Span));
        var off=BellyShape.Deform(upper,t,1,p);
        int builds=BellyShape.FieldBuildCount;
        p.UpperFieldFadeEnabled=true;
        var on=BellyShape.Deform(upper,t,1,p);
        check("Changing top fade rebuilds the cached shape field", BellyShape.FieldBuildCount==builds+1);
        check("Top fade toggle changes upper geometry", Vector3.Distance(off,on)>1e-6f);
        check("Top fade toggle leaves the lower surface unchanged", Vector3.Distance(before,BellyShape.Deform(lower,t,1,p))<1e-6f);
        p.UpperBoneFilterEnabled=true;
        var roundtrip=JsonSerializer.Deserialize<VtxSettings>(JsonSerializer.Serialize(p))!;
        check("All three restriction switches round trip", roundtrip.BreastExclusionEnabled && roundtrip.UpperBoneFilterEnabled && roundtrip.UpperFieldFadeEnabled);
        check("Upper material height release remains active", VirtualAxisMath.MaterialSurfaceWeight(.5f,new Vector3(0,VirtualAxisMath.UpperStart(t,p)+p.UpperTransitionWidth*t.Span,.7f),t,1,p)==0);
    }
}
