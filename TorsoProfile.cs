using System.Numerics;

namespace ALPregnancy;

// Rest-mesh modelling landmarks, not clinical measurements.
internal sealed class TorsoProfile
{
    public float PelvicFloor { get; init; }
    public float Pubis { get; init; }
    public float Navel { get; init; }
    public float Ribs { get; init; }
    public float SkinNavelY { get; private set; } = float.NaN;
    public float SkinNavelZ { get; private set; } = float.NaN;
    public float SampleMin { get; init; }
    public float SampleMax { get; init; }
    public float[] Front { get; init; }
    public float[] Back { get; init; }
    public float[] Width { get; init; }
    public float Span => Ribs - Pubis;

    internal static TorsoProfile Build(IReadOnlyList<Vector3> vertices, float floor, float navel, float upperSpine)
    {
        if (vertices.Count < 50 || !(floor < navel && navel < upperSpine))
            throw new ArgumentException("Torso landmarks or readable surface are missing.");
        float pubis = floor + (navel - floor) * 0.15f;
        float ribs = upperSpine - (upperSpine - floor) * 0.1f;
        float span = ribs - pubis;
        const int count = 21;
        float step = (upperSpine - floor) / (count - 1);
        var front = new float[count]; var back = new float[count]; var width = new float[count];
        for (int i = 0; i < count; i++)
        {
            float y = floor + step * i;
            var band = vertices.Where(v => MathF.Abs(v.Y-y) <= step * 1.25f).ToArray();
            if (band.Length < 8) band = vertices.OrderBy(v => MathF.Abs(v.Y-y)).Take(80).ToArray();
            var central = band.Where(v => MathF.Abs(v.X) < span * 0.13f).Select(v => v.Z).OrderBy(z => z).ToArray();
            if (central.Length < 4) central = band.Select(v => v.Z).OrderBy(z => z).ToArray();
            front[i] = Quantile(central, .88f);
            back[i] = Quantile(central, .12f);
            width[i] = MathF.Max(span * .2f, Quantile(band.Select(v => MathF.Abs(v.X)).OrderBy(x => x).ToArray(), .97f));
        }
        // Remove small details from the reference wall, so displacement preserves
        // the actual navel depression instead of flattening it onto an ellipsoid.
        var profile = new TorsoProfile { PelvicFloor=floor, Pubis=pubis, Navel=navel, Ribs=ribs,
            SampleMin=floor, SampleMax=upperSpine, Front=Filter(front), Back=Filter(back), Width=Filter(width) };
        // Locate an actual anterior midline depression in the rest mesh. The
        // waist bone is only a growth-frame datum, not the skin navel itself.
        float best=span*.008f;profile.SkinNavelY=navel+.09f*span;
        foreach(var v in vertices)
        {
            if(MathF.Abs(v.X)>span*.035f || v.Y<navel-.12f*span || v.Y>navel+.20f*span || v.Z<profile.AxisAt(v.Y))continue;
            float score=(profile.WallAt(v.X,v.Y)-v.Z)*(1-MathF.Abs(v.X)/(span*.07f));
            if(score>best){best=score;profile.SkinNavelY=v.Y;profile.SkinNavelZ=v.Z;}
        }
        return profile;
    }
    internal float FrontAt(float y) => Sample(Front,y);
    internal float BackAt(float y) => Sample(Back,y);
    internal float WidthAt(float y) => Sample(Width,y);
    internal float AxisAt(float y) => (FrontAt(y)+BackAt(y))*.5f;
    internal float WallAt(float x,float y)
    {
        float axis=AxisAt(y), radius=MathF.Max(1e-5f,WidthAt(y));
        return axis+(FrontAt(y)-axis)*MathF.Sqrt(MathF.Max(0,1-x*x/(radius*radius)));
    }
    private float Sample(float[] values,float y)
    {
        float t=Math.Clamp((y-SampleMin)/(SampleMax-SampleMin),0,1)*(values.Length-1);
        int i=Math.Min(values.Length-2,(int)t); t-=i;
        float delta=values[i+1]-values[i];
        float m0=Slope(i>0 ? values[i]-values[i-1] : delta,delta);
        float m1=Slope(delta,i+2<values.Length ? values[i+2]-values[i+1] : delta);
        return (2*t*t*t-3*t*t+1)*values[i]+(t*t*t-2*t*t+t)*m0+(-2*t*t*t+3*t*t)*values[i+1]+(t*t*t-t*t)*m1;
    }
    private static float Slope(float a,float b) => a*b<=0 ? 0 : 2*a*b/(a+b);
    private static float Quantile(float[] values,float t) => values[Math.Clamp((int)((values.Length-1)*t),0,values.Length-1)];
    private static float[] Filter(float[] values)
    {
        for(int pass=0;pass<2;pass++)
        {
            var next=new float[values.Length];
            for(int i=0;i<values.Length;i++)
                next[i]=(values[Math.Max(0,i-2)]+2*values[Math.Max(0,i-1)]+4*values[i]+2*values[Math.Min(values.Length-1,i+1)]+values[Math.Min(values.Length-1,i+2)])/10;
            values=next;
        }
        return values;
    }
}
