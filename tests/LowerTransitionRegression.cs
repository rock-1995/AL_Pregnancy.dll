using ALPregnancy;
using System.Text.Json;

internal static class LowerTransitionRegression
{
    internal static void Run(Action<string,bool> check)
    {
        bool exact=true, monotone=true, ordered=true, bounded=true, endpoints=true;
        for(int j=0;j<=80;j++)
        {
            float b=-2+j*.05f, previous=-1;
            for(int i=0;i<=1000;i++)
            {
                float u=i/1000f, w=VirtualAxisMath.LowerAttachment(u,b);
                bounded &= float.IsFinite(w) && w>=0 && w<=1;
                monotone &= w>=previous; previous=w;
                if(b<0)ordered &= w+1e-6f>=VirtualAxisMath.LowerAttachment(u,0);
                if(b>0)ordered &= w-1e-6f<=VirtualAxisMath.LowerAttachment(u,0);
                exact &= BitConverter.SingleToInt32Bits(VirtualAxisMath.LowerAttachment(u,0)) == BitConverter.SingleToInt32Bits(u*u*(3-2*u));
            }
            endpoints &= VirtualAxisMath.LowerAttachment(-.1f,b)==0 && VirtualAxisMath.LowerAttachment(1.1f,b)==1;
        }
        check("Zero bias exactly retains the old cubic attachment",exact);
        check("Every supported bias is monotone through the lower interval",monotone);
        check("Bias direction consistently advances or delays attachment",ordered);
        check("All supported attachment weights remain finite and bounded",bounded);
        check("All biases retain the original endpoints",endpoints);
        const float e=1e-5f;
        check("Extreme biases retain smooth zero-slope endpoints",new[]{-2f,0f,2f}.All(b=>VirtualAxisMath.LowerAttachment(e,b)/e<.001f && (1-VirtualAxisMath.LowerAttachment(1-e,b))/e<.001f));
        check("Out of range bias clamps to supported range",VirtualAxisMath.LowerAttachment(.4f,-99)==VirtualAxisMath.LowerAttachment(.4f,-2) && VirtualAxisMath.LowerAttachment(.4f,99)==VirtualAxisMath.LowerAttachment(.4f,2));
        check("Invalid bias falls back to original curve",new[]{float.NaN,float.NegativeInfinity,float.PositiveInfinity}.All(b=>VirtualAxisMath.LowerAttachment(.4f,b)==VirtualAxisMath.LowerAttachment(.4f,0)));
        var legacy=JsonSerializer.Deserialize<VtxSettings>("{\"virtualAxisStrength\":0.7}")!;
        check("Existing settings without the field preserve the old transition",legacy.LowerTransitionBias==0 && legacy.VirtualAxisStrength==.7f);
        legacy.LowerTransitionBias=1.25f;
        var saved=JsonSerializer.Serialize(legacy);
        check("New parameter survives JSON save/load",saved.Contains("\"lowerTransitionBias\":1.25") && JsonSerializer.Deserialize<VtxSettings>(saved)!.LowerTransitionBias==1.25f);
        var torso=new TorsoProfile {PelvicFloor=-1.4f,Navel=0,Ribs=1.3f};
        var p=new VtxSettings(); bool top=true, floor=true, unmoved=true, disabled=true;
        foreach(float b in new[]{-2f,-1f,0f,1f,2f})
        {
            p.LowerTransitionBias=b; p.VirtualAxisStrength=1;
            top &= VirtualAxisMath.SurfaceWeight(.5f*torso.Span,torso.PelvicFloor+.8f*torso.Span,torso,p)==VirtualAxisMath.Weight(.5f*torso.Span,torso.Span,p);
            floor &= VirtualAxisMath.SurfaceWeight(torso.Span,torso.PelvicFloor,torso,p)==0;
            unmoved &= VirtualAxisMath.SurfaceWeight(0,0,torso,p)==0;
            p.VirtualAxisStrength=0;
            disabled &= VirtualAxisMath.SurfaceWeight(torso.Span,0,torso,p)==0;
        }
        check("Bias leaves the upper abdomen influence unchanged",top);
        check("Bias retains pelvic-floor native attachment",floor);
        check("Bias cannot affect unmoved skin",unmoved);
        check("Disabling virtual skinning still cancels the biased transition",disabled);
        p=new VtxSettings();bool unchanged=true;
        foreach(float bias in new[]{-2f,0f,2f})for(int i=0;i<=1000;i++)
        {
            p.LowerTransitionBias=bias;float y=torso.PelvicFloor+torso.Span*i/1000;
            float old=VirtualAxisMath.Weight(.2f*torso.Span,torso.Span,p)*VirtualAxisMath.LowerAttachment(Math.Clamp((y-torso.PelvicFloor-.02f*torso.Span)/MathF.Max(torso.Span*.6f,1e-5f),0,1),bias);
            unchanged &= MathF.Abs(old-VirtualAxisMath.SurfaceWeight(.2f*torso.Span,y,torso,p))<2e-6f;
        }
        check("Default lower start/width preserve the full prior lower curve",unchanged);
        p.LowerTransitionStart=.20f;p.LowerTransitionWidth=.3f;p.LowerTransitionBias=0;
        check("Lower height slider moves the native attachment endpoint",VirtualAxisMath.SurfaceWeight(torso.Span,torso.PelvicFloor+.20f*torso.Span,torso,p)==0);
        check("Lower width slider moves the full-attachment endpoint",VirtualAxisMath.SurfaceWeight(torso.Span,torso.PelvicFloor+.50f*torso.Span,torso,p)>.999f);
    }
}
