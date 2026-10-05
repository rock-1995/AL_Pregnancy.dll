using System.Numerics;

namespace ALPregnancy;

// A growing internal egg displaces a measured abdominal wall. Rising fundus
// height expands the affected region; it does not drag the skin upward.
internal static class PriorBellyShape
{
    internal readonly record struct Egg(float Bottom,float Top,float HalfWidth,float Depth,float AnteriorOffset,float UpperFullness);
    internal static float Smooth(float t)
    {
        t=Math.Clamp(t,0,1);
        return Math.Clamp(t*t*t*(t*(t*6-15)+10),0,1);
    }
    internal static float BoneInfluence(float bellyWeight,float legWeight)
    {
        if(bellyWeight<=0) return 0;
        return Smooth(bellyWeight/.15f)*Smooth((bellyWeight/MathF.Max(bellyWeight+legWeight,1e-6f)-.35f)/.4f);
    }
    internal static float BreastRestore(float weight,float strength) => Smooth(Math.Clamp(weight,0,1)*MathF.Max(0,strength));

    internal static Egg Growth(TorsoProfile torso,float stage,VtxSettings p)
    {
        stage=Math.Clamp(stage,0,1);
        float span=torso.Span;
        float top=Curve(stage,torso.PelvicFloor+.02f*span,torso.Pubis,torso.Navel,
            torso.Navel+(torso.Ribs-torso.Navel)*Math.Clamp(p.UpperReach,.75f,1.2f));
        float settling=Math.Clamp(p.LateSettle,0,1)*.12f*span*Smooth((stage-.85f)/.15f);
        float bottom=torso.PelvicFloor-.05f*span-settling;
        top-=settling;
        float width=span*Curve(stage,.025f,.085f,.30f,.48f)*Math.Clamp(p.GrowthWidth,.65f,1.4f);
        float fullness=Math.Clamp(p.GrowthFullness,.5f,1.6f);
        float depth=span*Curve(stage,.025f,.09f,.36f,.50f)*fullness;
        // A vertical egg remains longer than its default transverse/depth axes.
        // Its centre also moves anteriorly as it emerges from the pelvis; forward
        // prominence therefore need not come from an excessively long depth axis.
        float anterior=span*Curve(stage,0,0,.05f,.25f)*fullness;
        // Small early pear -> round -> upper-full longitudinal egg. The skin
        // remains at its original height while this internal volume rises.
        float upperFullness=.16f*(1-Smooth(stage/.33f))+.28f*Smooth((stage-.56f)/.44f);
        return new Egg(bottom,top,width,depth,anterior,upperFullness);
    }
    internal static Vector3 Deform(Vector3 original,TorsoProfile torso,float stage,VtxSettings p)
    {
        if(stage<=0) return original;
        float sampleY=torso.Navel+(original.Y-torso.Navel)/Math.Clamp(p.VerticalRange,.6f,1f);
        float pressure=Pressure(original.X,sampleY,torso,Growth(torso,stage,p));
        if(pressure<=0) return original;
        float lower=Smooth((stage-.56f)/.44f)*MathF.Exp(-MathF.Pow((original.Y-(torso.Navel-.21f*torso.Span))/(.26f*torso.Span),2));
        float push=pressure*FrontGate(original,torso)*(1+.25f*lower);
        float side=push*.12f*original.X/MathF.Max(torso.WidthAt(original.Y),1e-5f);
        return original+new Vector3(side,0,push);
    }
    internal static float ClothingFootprint(Vector3 original,TorsoProfile torso,float stage,VtxSettings p)
    {
        if(stage<=0) return 0;
        float sampleY=torso.Navel+(original.Y-torso.Navel)/Math.Clamp(p.VerticalRange,.6f,1f);
        float pressure=Pressure(original.X,sampleY,torso,Growth(torso,stage,p));
        return Smooth(pressure/(torso.Span*.025f))*FrontGate(original,torso);
    }
    private static float FrontGate(Vector3 v,TorsoProfile torso)
        => Smooth((v.Z-torso.AxisAt(v.Y))/MathF.Max((torso.FrontAt(v.Y)-torso.AxisAt(v.Y))*.8f,1e-5f));
    private static float Pressure(float x,float y,TorsoProfile torso,Egg egg)
    {
        // Abdominal wall tension spreads a contact load along the skin. Merely
        // clipping the egg at its wall intersection creates a steep shoulder,
        // even with a smooth pointwise contact function. A compact binomial
        // convolution broadens that transition before sampling the game mesh.
        float load=SpreadContact(x,y,torso,egg);
        // Continue the loaded wall to its rib attachment with matching value,
        // and slope, rather than multiplying two shrinking envelopes.
        // Only emerging late-stage load needs this upper wall tension bridge.
        float lateralRibRise=Smooth(MathF.Abs(x)/(torso.Span*.36f));
        float attachment=torso.Ribs+(.10f-.28f*lateralRibRise)*torso.Span;
        float shoulder=torso.Navel+.30f*(torso.Ribs-torso.Navel);
        if(y>=attachment) return 0;
        if(y>shoulder)
        {
            float length=attachment-shoulder;
            float h=torso.Span*.005f;
            float a=SpreadContact(x,shoulder,torso,egg);
            float lo=SpreadContact(x,shoulder-h,torso,egg);
            float hi=SpreadContact(x,shoulder+h,torso,egg);
            float slope=(hi-lo)/(2*h);
            float t=(y-shoulder)/length;
            float t2=t*t,t3=t2*t;
            float bridge=a*(1-3*t2+2*t3)+slope*length*(t-2*t2+t3);
            // Blend by internal fundus height; the original early/mid contact
            // footprint must not suddenly acquire a long upper-abdomen tail.
            float engaged=Smooth((egg.Top-shoulder)/MathF.Max(torso.Ribs-shoulder,1e-5f));
            load=load*Smooth((attachment-y)/length)*(1-engaged)+MathF.Max(0,bridge)*engaged;
        }
        float lowerAttachment=Smooth((y-torso.PelvicFloor)/(torso.Pubis-torso.PelvicFloor+.06f*torso.Span));
        return load*lowerAttachment;
    }
    private static float SpreadContact(float x,float y,TorsoProfile torso,Egg egg)
    {
        float step=torso.Span*.085f;
        return (Contact(x,y-3*step,torso,egg)+6*Contact(x,y-2*step,torso,egg)
            +15*Contact(x,y-step,torso,egg)+20*Contact(x,y,torso,egg)
            +15*Contact(x,y+step,torso,egg)+6*Contact(x,y+2*step,torso,egg)
            +Contact(x,y+3*step,torso,egg))/64;
    }
    private static float Contact(float x,float y,TorsoProfile torso,Egg egg)
    {
        float halfHeight=(egg.Top-egg.Bottom)*.5f;
        if(halfHeight<=0) return 0;
        float u=(y-(egg.Top+egg.Bottom)*.5f)/halfHeight;
        if(MathF.Abs(u)>=1) return 0;
        // Widen the fundus without hollowing the already accepted lower belly.
        float fullness=1+egg.UpperFullness*MathF.Max(0,u)*Smooth(MathF.Max(0,u)/.25f);
        float radius=egg.HalfWidth*fullness;
        float cross=1-u*u-x*x/(radius*radius);
        if(cross<=0) return 0;
        float surface=torso.AxisAt(y)+egg.AnteriorOffset+egg.Depth*fullness*MathF.Sqrt(cross);
        float penetration=surface-torso.WallAt(x,y);
        if(penetration<=0) return 0;
        return penetration*Smooth(penetration/(torso.Span*.12f));
    }
    // Shape-preserving cubic interpolation through early/mid/late keys.
    private static float Curve(float stage,float a,float b,float c,float d)
    {
        const float k1=.33f,k2=.56f;
        float s0=(b-a)/k1,s1=(c-b)/(k2-k1),s2=(d-c)/(1-k2);
        float m1=Slope(s0,s1),m2=Slope(s1,s2);
        if(stage<k1) return Hermite(a,b,s0,m1,stage/k1,k1);
        if(stage<k2) return Hermite(b,c,m1,m2,(stage-k1)/(k2-k1),k2-k1);
        return Hermite(c,d,m2,s2,(stage-k2)/(1-k2),1-k2);
    }
    private static float Slope(float a,float b) => a*b<=0 ? 0 : 2*a*b/(a+b);
    private static float Hermite(float a,float b,float ma,float mb,float t,float length)
        => (2*t*t*t-3*t*t+1)*a+(t*t*t-2*t*t+t)*ma*length+(-2*t*t*t+3*t*t)*b+(t*t*t-t*t)*mb*length;
}
