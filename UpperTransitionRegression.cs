using ALPregnancy;
using System.Numerics;
using System.Text.Json;

internal static class UpperTransitionRegression
{
    internal static void Run(Action<string,bool> check)
    {
        var t=new TorsoProfile {PelvicFloor=-1.45f,Pubis=-1.23f,Navel=0,Ribs=1.5f,SampleMin=-1.6f,SampleMax=2,
            Front=new[]{.6f,.65f,.7f},Back=new[]{-.4f,-.4f,-.4f},Width=new[]{.8f,.9f,1f}};
        var p=new VtxSettings();
        bool lower=true,finite=true,monotone=true,ends=true,ordered=true;
        foreach(float bias in new[]{-2f,-1f,0f,1f,2f})
        {
            p.UpperTransitionBias=bias;
            float prev=1;
            for(int i=0;i<=1000;i++)
            {
                float u=i/1000f,w=VirtualAxisMath.UpperRelease(u,bias);
                finite &= float.IsFinite(w)&&w>=0&&w<=1;
                monotone &= w<=prev;prev=w;
                float zero=VirtualAxisMath.UpperRelease(u,0);
                ordered &= bias<0 ? w>=zero-1e-6f : bias>0 ? w<=zero+1e-6f : w==zero;
                float y=t.PelvicFloor+(VirtualAxisMath.UpperStart(t,p)-t.PelvicFloor)*u;
                lower &= VirtualAxisMath.MaterialSurfaceWeight(.5f,new(0,y,.7f),t,1,p)==VirtualAxisMath.SurfaceWeight(.5f,y,t,p);
            }
            ends &= VirtualAxisMath.UpperRelease(0,bias)==1&&VirtualAxisMath.UpperRelease(1,bias)==0;
        }
        check("Upper height curve stays finite and monotone through every bias",finite&&monotone);
        check("Negative upper bias holds support and positive releases it earlier",ordered);
        check("Upper height curve preserves its full-support and native endpoints",ends);
        check("New upper formula preserves every weight below its chosen start",lower);
        p.UpperTransitionBias=0;
        float start=VirtualAxisMath.UpperStart(t,p),end=start+p.UpperTransitionWidth*t.Span;
        float Evaluate(float y,float d=.5f)=>VirtualAxisMath.MaterialSurfaceWeight(d,new(0,y,.7f),t,1,p);
        check("Upper exit and all higher vertices return to native skinning",Evaluate(end)==0&&Evaluate(end+t.Span)==0);
        check("Upper join has no position-weight jump at its lower edge",MathF.Abs(Evaluate(start+t.Span*1e-5f)-Evaluate(start))<1e-6f);
        check("Upper exit has a zero-slope limit",Evaluate(end-t.Span*1e-5f)<1e-7f);
        check("Unmoved vertices and zero pregnancy cannot acquire new attachment",Evaluate((start+end)*.5f,0)==0&&VirtualAxisMath.MaterialSurfaceWeight(.5f,new(0,0,.7f),t,0,p)==0);
        bool scales=true;
        float ymid=(start+end)*.5f,full=Evaluate(ymid);
        foreach(float strength in new[]{0,.1f,.4f,.8f,1f})
        {
            p.VirtualAxisStrength=strength;
            scales &= MathF.Abs(Evaluate(ymid)-full*strength)<2e-6f;
        }
        check("Global virtual strength still scales or disables the new band",scales);
        p.VirtualAxisStrength=1;
        p.UpperTransitionWidth=.7f;
        check("A wider upper band retains support beyond the former exit",Evaluate(end)>.01f);
        p.UpperTransitionStart=.8f;
        check("Moving upper start leaves the old upper region on the lower formula",Evaluate(ymid)==VirtualAxisMath.SurfaceWeight(.5f,ymid,t,p));
        p.UpperTransitionStart=.15f;p.UpperTransitionWidth=.55f;
        float before=Evaluate(ymid);p.UpperFoldSupport=4;
        check("Legacy geometric-support setting cannot alter new material weights",Evaluate(ymid)==before);
        p.UpperTransitionWidth=float.NaN;p.UpperTransitionJoin=float.NaN;p.UpperTransitionActivation=float.NaN;
        check("Invalid new values use finite defaults",float.IsFinite(Evaluate(ymid))&&MathF.Abs(Evaluate(ymid)-before)<1e-6f);
        p=new VtxSettings{LowerTransitionStart=.11f,LowerTransitionWidth=.31f,LowerTransitionBias=-.4f,
            UpperTransitionStart=.27f,UpperTransitionWidth=.63f,UpperTransitionBias=1.2f,UpperTransitionJoin=.35f,UpperTransitionActivation=.04f};
        string json=JsonSerializer.Serialize(p);
        check("All eight band controls round-trip through existing settings JSON",JsonSerializer.Serialize(JsonSerializer.Deserialize<VtxSettings>(json))==json);
        var old=JsonSerializer.Deserialize<VtxSettings>("{\"lowerTransitionBias\":1.25,\"upperFoldSupport\":4}")!;
        check("Old files keep lower bias and receive the original lower start/width",old.LowerTransitionBias==1.25f&&old.LowerTransitionStart==.02f&&old.LowerTransitionWidth==.6f);
        check("New presets retire per-pose geometric support",new VtxSettings().UpperFoldSupport==0);
    }
}
