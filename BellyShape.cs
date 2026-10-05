using System.Numerics;
using System.Runtime.CompilerServices;

namespace ALPregnancy;

// Bind-space internal-volume contact with one fixed full-term membrane domain.
// Only parameter/profile changes build the field; vertices only sample it.
internal static class BellyShape
{
    internal readonly record struct Egg(float Bottom,float Top,float HalfWidth,float Depth,float AnteriorOffset,float UpperFullness=0);
    private readonly record struct Key(float Stage,float Fullness,float Width,float Reach,float Range,float Settle,float Smoothing,float Sag,float MidVolume,float Lift,float Clearance,bool UpperFade);
    private sealed class Cache { internal Key Key; internal Field Value; }
    private static readonly ConditionalWeakTable<TorsoProfile,Cache> Fields=new();
    internal static int FieldBuildCount { get; private set; }
    internal static float Smooth(float t) { t=Math.Clamp(t,0,1);return t*t*t*(t*(t*6-15)+10); }
    internal static float BoneInfluence(float bellyWeight,float legWeight)
        => bellyWeight<=0 ? 0 : Smooth(bellyWeight/.15f)*Smooth((bellyWeight/MathF.Max(bellyWeight+legWeight,1e-6f)-.35f)/.4f);
    // Preserve the lower abdomen's native bone filter. The upper trial uses
    // full shape influence; original height is measured in the shared torso frame.
    internal static float DeformationInfluence(float native, float originalY, TorsoProfile torso, VtxSettings p)
        => !p.UpperBoneFilterEnabled && originalY > torso.Navel ? 1f : native;
    internal static float BreastRestore(float weight,float strength)=>Smooth(Math.Clamp(weight,0,1)*MathF.Max(0,strength));

    internal static Egg Growth(TorsoProfile torso,float stage,VtxSettings p)
    {
        float s=Math.Clamp(stage,0,1),scale=torso.Span/2.7424295f;
        // User-reviewed v3 controls, expressed relative to the original waist datum.
        float[] keys={0,.33333334f,.44444445f,.5555556f,1};
        float[] cy={-.7411765f,-.7411765f,-.545f,-.125f,.04f};
        float[] cz={.15882353f,.15882353f,.34f,.40f,.58f};
        float mid=Math.Clamp(p.MidVolume,.75f,1.5f);
        float[] rx={.02f,.24705882f,.45f*mid,.71f,1.06f};
        float[] ry={.02f,.24117647f,.59f*mid,1.013f,1.256f};
        float[] rz={.02f,.24705882f,.47f*mid,.72f,1.06f};
        cy[4]=-.05f+.09f*Math.Clamp(p.LowerPoleLift,0,2);
        ry[4]-=.09f*Math.Clamp(p.LowerPoleLift,0,2);
        float Interpolate(float[] a)
        {
            int i=0;while(i<3 && s>keys[i+1])i++;
            float t=Math.Clamp((s-keys[i])/(keys[i+1]-keys[i]),0,1);
            return a[i]+(a[i+1]-a[i])*t;
        }
        float y=torso.Navel+Interpolate(cy)*scale,h=Interpolate(ry)*scale;
        float top=y+h,bottom=y-h;
        float late=Smooth((s-keys[3])/(1-keys[3]));
        top+=(Math.Clamp(p.UpperReach,.75f,1.2f)-1)*torso.Span*.45f*late;
        float settle=Math.Clamp(p.LateSettle,0,1)*.12f*torso.Span*Smooth((s-.85f)/.15f);
        float range=Math.Clamp(p.VerticalRange,.6f,1.2f);
        bottom=torso.Navel+(bottom-torso.Navel)*range-settle;
        top=torso.Navel+(top-torso.Navel)*range-settle;
        float fullness=Math.Clamp(p.GrowthFullness,.5f,1.6f);
        return new Egg(bottom,top,Interpolate(rx)*scale*Math.Clamp(p.GrowthWidth,.5f,2),
            Interpolate(rz)*scale*fullness,
            torso.AxisAt(torso.Navel)+(Interpolate(cz)+.08822574f)*scale*fullness);
    }
    private static Field Get(TorsoProfile t,float stage,VtxSettings p)
    {
        var key=new Key(stage,p.GrowthFullness,p.GrowthWidth,p.UpperReach,p.VerticalRange,p.LateSettle,p.WallSmoothing,p.SagStrength,p.MidVolume,p.LowerPoleLift,p.SkinClearance,p.UpperFieldFadeEnabled);
        if(!Fields.TryGetValue(t,out var cache)) {cache=new Cache();Fields.Add(t,cache);}
        if(cache.Value==null || cache.Key!=key) {cache.Key=key;cache.Value=new Field(t,stage,p);FieldBuildCount++;}
        return cache.Value;
    }
    internal static Vector3 Deform(Vector3 original,TorsoProfile torso,float stage,VtxSettings p)
    {
        if(stage<=0)return original;
        var f=Get(torso,stage,p);
        var deformed=DeformBase(original,torso,f);
        return NavelPatch(original,deformed,torso,stage,p,f);
    }
    private static Vector3 DeformBase(Vector3 original,TorsoProfile torso,Field f)
    {
        f.Sample(original,out float push,out float drop,out float angle);
        if(push<torso.Span*.0001f && drop<torso.Span*.0001f)return original;
        // Advect skin down the smooth envelope instead of translating the old
        // radial profile, which would flatten its lower cap. Preserve radial
        // surface detail (including the navel depression) during this transport.
        float y=original.Y-drop;
        f.SampleAtHeight(y,angle,out float surfacePush,out _);
        float oldAxis=torso.AxisAt(original.Y),newAxis=torso.AxisAt(y);
        float radius=MathF.Sqrt(original.X*original.X+(original.Z-oldAxis)*(original.Z-oldAxis));
        radius+=f.WallRadius(y,angle)-f.WallRadius(original.Y,angle)+surfacePush;
        return new Vector3(radius*MathF.Sin(angle),y,MathF.Max(original.Z,newAxis+radius*MathF.Cos(angle)));
    }
    internal static float NavelStageResponse(float stage,VtxSettings p)
        => stage<=0 ? 0 : p.NavelPreviewFull ? 1 : Smooth((stage-Math.Clamp(p.NavelStart,.3f,.95f))/(1-Math.Clamp(p.NavelStart,.3f,.95f)));

    private static Vector3 NavelPatch(Vector3 original,Vector3 deformed,TorsoProfile torso,float stage,VtxSettings p,Field f)
    {
        // A reliable depression must exist in the ORIGINAL mesh. No fallback
        // waist landmark is allowed to manufacture a new navel.
        if(!float.IsFinite(torso.SkinNavelZ) || (p.NavelEversion<=0 && p.NavelProportion<=0))return deformed;
        float radius=Math.Clamp(p.NavelRadius,.015f,.08f)*torso.Span;
        float u=original.X,v=original.Y-torso.SkinNavelY;
        float r=MathF.Sqrt(u*u/(radius*radius)+v*v/(radius*radius*1.69f));
        if(r>=1 || original.Z<=torso.AxisAt(original.Y))return deformed;
        float late=NavelStageResponse(stage,p);
        float expansion=Smooth(Vector3.Distance(original,deformed)/(torso.Span*.12f));
        float mask=Smooth(1-r)*late*expansion;
        if(mask<=0)return deformed;
        float y=torso.SkinNavelY,eps=torso.Span*.003f;
        Vector3 Wall(float x,float yy)=>new(x,yy,torso.WallAt(x,yy));
        var dx=(DeformBase(Wall(eps,y),torso,f)-DeformBase(Wall(-eps,y),torso,f))/(2*eps);
        var dy=(DeformBase(Wall(0,y+eps),torso,f)-DeformBase(Wall(0,y-eps),torso,f))/(2*eps);
        float sx=dx.X,sy=dy.Y;
        // Preserve local material spacing instead of widening a compressed pit
        // into a transverse slit. The compact mask leaves the surrounding wall.
        var correction=new Vector3(u*(Math.Clamp(sx,.85f,1.5f)-sx),v*(Math.Clamp(sy,.85f,1.5f)-sy),0)
            *(mask*Math.Clamp(p.NavelProportion,0,1));
        float depression=MathF.Min(0,original.Z-torso.WallAt(original.X,original.Y));
        float height=Math.Clamp(p.NavelHeight,0,.03f)*torso.Span*Smooth(1-r);
        // Eversion changes depth only. Tilting this local push down the belly
        // normal would fold already-compressed rows underneath the navel.
        correction+=Vector3.UnitZ*(mask*Math.Clamp(p.NavelEversion,0,2)*(height-depression));
        return deformed+correction;
    }
    internal static float ClothingFootprint(Vector3 original,TorsoProfile torso,float stage,VtxSettings p)
    {
        if(stage<=0)return 0;
        Get(torso,stage,p).Sample(original,out float push,out float drop,out _);
        return Smooth(MathF.Max(push,drop)/(torso.Span*.025f));
    }
    private sealed class Field
    {
        private const int Ny=97,Nt=65;
        private readonly float[] _push=new float[Ny*Nt],_drop=new float[Ny*Nt];
        private readonly TorsoProfile _torso;
        private readonly float _min,_max,_dy;
        private const float Dt=MathF.PI/(Nt-1);
        internal Field(TorsoProfile torso,float stage,VtxSettings p)
        {
            _torso=torso;float span=torso.Span,scale=span/2.7424295f;
            var egg=Growth(torso,stage,p);var first=Growth(torso,1f/3,p);var final=Growth(torso,1,p);
            // The smoothing domain always includes the anticipated full-term area,
            // even when the current small volume contacts only the lower abdomen.
            _min=MathF.Max(torso.PelvicFloor+.02f*span,MathF.Min(first.Bottom,final.Bottom)-.20f*span);
            _max=MathF.Max(first.Top,final.Top)+.24f*span;
            _dy=(_max-_min)/(Ny-1);
            var load=new float[Ny*Nt];var future=new float[Ny*Nt];var sine=new float[Nt];var cosine=new float[Nt];
            for(int j=0;j<Nt;j++){float a=-MathF.PI*.5f+j*Dt;sine[j]=MathF.Sin(a);cosine[j]=MathF.Cos(a);}
            float Radius(Egg e,float y,float axis,float sn,float cs)
            {
                float h=(e.Top-e.Bottom)*.5f,u=(y-(e.Top+e.Bottom)*.5f)/h;
                if(MathF.Abs(u)>=1)return -1;
                float a=sn*sn/(e.HalfWidth*e.HalfWidth)+cs*cs/(e.Depth*e.Depth);
                float b=2*cs*(axis-e.AnteriorOffset)/(e.Depth*e.Depth);
                float c=(axis-e.AnteriorOffset)*(axis-e.AnteriorOffset)/(e.Depth*e.Depth)-(1-u*u);
                float disc=b*b-4*a*c;
                return disc<0 ? -1 : (-b+MathF.Sqrt(disc))/(2*a);
            }
            float peak=0,futurePeak=0;
            for(int i=1;i<Ny-1;i++)
            {
                float y=_min+i*_dy,axis=torso.AxisAt(y),depth=MathF.Max(.01f*span,(torso.FrontAt(y)-torso.BackAt(y))*.5f),width=torso.WidthAt(y);
                for(int j=1;j<Nt-1;j++)
                {
                    float wall=1/MathF.Sqrt(sine[j]*sine[j]/(width*width)+cosine[j]*cosine[j]/(depth*depth));
                    float radius=Radius(egg,y,axis,sine[j],cosine[j]);
                    if(stage>=1f/3)radius=MathF.Max(radius,Radius(first,y,axis,sine[j],cosine[j]));
                    float value=radius>0 ? MathF.Max(0,radius+Math.Clamp(p.SkinClearance,0,.2f)*scale-wall):0;
                    load[i*Nt+j]=_push[i*Nt+j]=value;peak=MathF.Max(peak,value);
                    float finalRadius=Radius(final,y,axis,sine[j],cosine[j]);
                    float finalLoad=finalRadius>0 ? MathF.Max(0,finalRadius+Math.Clamp(p.SkinClearance,0,.2f)*scale-wall):0;
                    future[i*Nt+j]=finalLoad;futurePeak=MathF.Max(futurePeak,finalLoad);
                }
            }
            if(peak==0)return;
            float smoothing=Math.Clamp(p.WallSmoothing,0,2);
            float late=Smooth((stage-.5555556f)/(1-.5555556f));
            // Engage the future abdominal footprint in early/mid smoothing,
            // rather than merely diffusing a small isolated lower-belly bump.
            float shared=Math.Clamp(smoothing,0,1)*.90f*(1-late)*Smooth((stage-1f/3)/(1f/9));
            if(futurePeak>0 && shared>0)
                for(int k=0;k<load.Length;k++)
                    load[k]=_push[k]=MathF.Max(load[k],future[k]*(peak/futurePeak)*shared);
            float ly=(.25f+smoothing*(.50f*(1-late)+.06f*late))*scale;
            float lt=.24f+.08f*smoothing;
            float ay=ly*ly/(_dy*_dy),at=lt*lt/(Dt*Dt),denom=1+2*ay+2*at;
            // Projected SOR for the contact obstacle. It runs only on shape rebuild.
            for(int iteration=0;iteration<900;iteration++)
            {
                float change=0;
                for(int color=0;color<2;color++)
                for(int i=1;i<Ny-1;i++)
                for(int j=1+((i+color)&1);j<Nt-1;j+=2)
                {
                    int k=i*Nt+j;
                    float target=(ay*(_push[k-Nt]+_push[k+Nt])+at*(_push[k-1]+_push[k+1]))/denom;
                    float next=MathF.Max(load[k],_push[k]+1.65f*(target-_push[k]));
                    change=MathF.Max(change,MathF.Abs(next-_push[k]));_push[k]=next;
                }
                if(change<span*1e-6f)break;
            }
            for(int i=0;i<Ny;i++)
            for(int j=0;j<Nt;j++)
            {
                int k=i*Nt+j;
                float edge=Smooth(i*_dy/(.16f*span))*(p.UpperFieldFadeEnabled ? Smooth((Ny-1-i)*_dy/(.12f*span)) : 1f)*Smooth(MathF.Min(j,Nt-1-j)*Dt/.30f);
                _push[k]*=edge;
                _drop[k]=Math.Clamp(p.SagStrength,0,2)*.50f*_push[k]*cosine[j]*cosine[j];
            }
            // Gravity extends the lower surface while retaining internal contact.
            // The same SagStrength drives both the envelope and skin advection.
            if(p.SagStrength>0)
            {
                var basePush=(float[])_push.Clone();
                float shift=.22f*scale*Math.Clamp(p.SagStrength,0,2)/_dy;
                for(int i=1;i<Ny-1;i++)
                for(int j=1;j<Nt-1;j++)
                {
                    float u=MathF.Min(Ny-1,i+shift);int lo=Math.Min(Ny-2,(int)u);float f=u-lo;
                    float spread=.85f*((1-f)*basePush[lo*Nt+j]+f*basePush[(lo+1)*Nt+j]);
                    float extra=MathF.Max(0,spread-basePush[i*Nt+j]);
                    _push[i*Nt+j]+=extra*Smooth(extra/(.035f*span))*Smooth(i*_dy/(.16f*span));
                }
            }
            // Bound the vertical gradient so sag cannot reverse the row ordering.
            for(int j=0;j<Nt;j++)
            {
                for(int i=1;i<Ny;i++)_drop[i*Nt+j]=MathF.Min(_drop[i*Nt+j],_drop[(i-1)*Nt+j]+.60f*_dy);
                for(int i=Ny-2;i>=0;i--)_drop[i*Nt+j]=MathF.Min(_drop[i*Nt+j],_drop[(i+1)*Nt+j]+.60f*_dy);
            }
        }
        internal float WallRadius(float y,float angle)
        {
            float sn=MathF.Sin(angle),cs=MathF.Cos(angle),w=_torso.WidthAt(y);
            float d=MathF.Max(.01f*_torso.Span,(_torso.FrontAt(y)-_torso.BackAt(y))*.5f);
            return 1/MathF.Sqrt(sn*sn/(w*w)+cs*cs/(d*d));
        }
        internal void Sample(Vector3 v,out float push,out float drop,out float angle)
        {
            angle=MathF.Atan2(v.X,v.Z-_torso.AxisAt(v.Y));
            SampleAtHeight(v.Y,angle,out push,out drop);
        }
        internal void SampleAtHeight(float y,float angle,out float push,out float drop)
        {
            push=drop=0;
            if(y<=_min || y>=_max || MathF.Abs(angle)>=MathF.PI*.5f)return;
            float yy=(y-_min)/_dy,tt=(angle+MathF.PI*.5f)/Dt;
            int i=Math.Min(Ny-2,(int)yy),j=Math.Min(Nt-2,(int)tt);float fy=yy-i,ft=tt-j;
            float SampleArray(float[] a) => (1-fy)*((1-ft)*a[i*Nt+j]+ft*a[i*Nt+j+1])+fy*((1-ft)*a[(i+1)*Nt+j]+ft*a[(i+1)*Nt+j+1]);
            push=SampleArray(_push);drop=SampleArray(_drop);
        }
    }
}


