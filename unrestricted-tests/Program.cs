using ALPregnancy;
using System.Numerics;
using System.Text.Json;
using System.Globalization;

int count=0;
void Check(string label,bool ok){if(!ok)throw new Exception("FAIL: "+label);count++;Console.WriteLine("PASS: "+label);}
foreach(var range in new[]{(-2f,2f),(0f,1f),(.02f,1.5f),(.001f,.15f),(10f,120f)})
foreach(float x in new[]{-1000f,-.8f,0f,.00001f,2.5f,100000f})
{
    var input=new ShapeInput();input.Synchronize(x);
    Check("Input reads exact finite value "+x,ShapeInput.TryRead(input.Text,out float parsed)&&parsed==x);
    for(int frame=0;frame<200;frame++)
    {
        float repainted=ShapeInput.FromSlider(x,ShapeInput.SliderPosition(x,range.Item1,range.Item2),range.Item1,range.Item2);
        if(repainted!=x)throw new Exception("Repaint changed unrestricted value");
        input.Synchronize(repainted);
    }
    Check("Repeated slider repaint preserves value "+x,ShapeInput.TryRead(input.Text,out float after)&&after==x);
}
Check("Scientific notation accepted",ShapeInput.TryRead("-1.25e3",out var scientific)&&scientific==-1250);
Check("Incomplete/nonfinite text is not committed",new[]{"", "-", "1e", "NaN", "Infinity", "1e999"}.All(s=>!ShapeInput.TryRead(s,out _)));
var state=new ShapeInput();state.Synchronize(5);state.Text="-";state.Synchronize(5);
Check("Repaint preserves in-progress edit",state.Text=="-");state.Synchronize(2);
Check("Reset or external value refreshes input",state.Text=="2");
var round=new VtxSettings();int k=0;
foreach(var prop in typeof(VtxSettings).GetProperties().Where(x=>x.PropertyType==typeof(float)))prop.SetValue(round,(++k%2==0?-1:1)*(1000f+k));
var loaded=JsonSerializer.Deserialize<VtxSettings>(JsonSerializer.Serialize(round));
Check("Every stored float survives unrestricted JSON round trip",typeof(VtxSettings).GetProperties().Where(x=>x.PropertyType==typeof(float)).All(p=>Equals(p.GetValue(round),p.GetValue(loaded))));

var t=new TorsoProfile{PelvicFloor=-1.4541378f,Pubis=-1.2360171f,Navel=0,Ribs=1.5064124f,SampleMin=-1.4541378f,SampleMax=1.8353624f,
    Front=Enumerable.Repeat(.72f,21).ToArray(),Back=Enumerable.Repeat(-.88f,21).ToArray(),Width=Enumerable.Repeat(1.2f,21).ToArray()};
var p=new VtxSettings {BellySag=0,SagStrength=0,NavelEversion=0,NavelProportion=0};
var points=(from yy in Enumerable.Range(0,60) from xx in new[]{0f,.2f,.5f} select new Vector3(xx,-1.2f+yy*.045f,.72f)).ToArray();
float Difference(string property,float a,float b,float stage=1)
{
    var field=typeof(VtxSettings).GetProperty(property);field.SetValue(p,a);
    var va=points.Select(v=>BellyShape.Deform(v,t,stage,p)).ToArray();field.SetValue(p,b);
    var vb=points.Select(v=>BellyShape.Deform(v,t,stage,p)).ToArray();
    if(vb.Any(v=>!float.IsFinite(v.X+v.Y+v.Z)))throw new Exception("Nonfinite shape: "+property);
    return va.Select((v,i)=>Vector3.Distance(v,vb[i])).Max();
}
foreach(var row in new[]{("GrowthFullness",1.6f,2.0f,1f),("GrowthWidth",2f,2.5f,1f),("UpperReach",1.2f,1.6f,1f),
    ("VerticalRange",1.2f,1.5f,1f),("MidVolume",1.5f,2f,4f/9),("SkinClearance",.2f,.35f,1f),
    ("LateSettle",1f,1.4f,1f),("LowerPoleLift",2f,2.5f,1f),("WallSmoothing",2f,3f,1f),
    ("SagStrength",0f,-.8f,1f),("SagStrength",2f,3f,1f)})
{
    p=new VtxSettings {BellySag=0,SagStrength=0,NavelEversion=0,NavelProportion=0};
    Check(row.Item1+" remains effective beyond prior range",Difference(row.Item1,row.Item2,row.Item3,row.Item4)>1e-5f);
}
// Negative legacy sag must preserve the zero boundary and monotone material rows.
foreach(float sag in new[]{-.8f,-3f,-20f})
{
    p=new VtxSettings{SagStrength=sag,BellySag=0,NavelEversion=0,NavelProportion=0};
    var seed=BellyShape.Growth(t,1f/3,p);var final=BellyShape.Growth(t,1,p);
    float min=MathF.Max(t.PelvicFloor+.02f*t.Span,MathF.Min(seed.Bottom,final.Bottom)-.20f*t.Span);
    float max=MathF.Max(seed.Top,final.Top)+.24f*t.Span,step=(max-min)/2000;
    var before=new Vector3(0,min-step,.72f);var after=new Vector3(0,min+step,.72f);
    var a=BellyShape.Deform(before,t,1,p);var b=BellyShape.Deform(after,t,1,p);
    Check("Negative sag preserves lower-boundary continuity "+sag,MathF.Abs((b.Y-a.Y)-2*step)<=1.3f*step);
    float last=a.Y;bool ordered=true;
    for(int i=0;i<=2001;i++){
        float y=min+i*step;float moved=BellyShape.Deform(new Vector3(0,y,.72f),t,1,p).Y;
        ordered&=float.IsFinite(moved)&&moved>last;last=moved;
    }
    Check("Negative sag preserves vertical row ordering "+sag,ordered);
}
p=new VtxSettings{AxisBlendStart=.1f,AxisBlendFull=.12f};
Check("Full blend uses the exact requested threshold",VirtualAxisMath.Weight(.12f*t.Span,t.Span,p)>.99999f);
Check("Narrow blend preserves its midpoint",Math.Abs(VirtualAxisMath.Weight(.11f*t.Span,t.Span,p)-.5f)<1e-5f);
p.AxisBlendStart=-.2f;p.AxisBlendFull=-.1f;
Check("Negative displacement thresholds are used directly",VirtualAxisMath.Weight(0,t.Span,p)==1);
p.AxisBlendStart=.2f;p.AxisBlendFull=.1f;
Check("Reversed thresholds are not reordered",VirtualAxisMath.Weight(.05f*t.Span,t.Span,p)==1&&VirtualAxisMath.Weight(.25f*t.Span,t.Span,p)==0);
foreach(float bias in new[]{-1000f,-20f,-4f,4f,20f,1000f})
{
    Check("Unrestricted bias finite at endpoints "+bias,VirtualAxisMath.LowerAttachment(0,bias)==0&&VirtualAxisMath.LowerAttachment(1,bias)==1);
    float v=VirtualAxisMath.LowerAttachment(.4f,bias);
    Check("Unrestricted bias changes curve "+bias,float.IsFinite(v)&&(bias>0?v<VirtualAxisMath.LowerAttachment(.4f,2):v>VirtualAxisMath.LowerAttachment(.4f,-2)));
}
p=new VtxSettings{LowerTransitionStart=0,LowerTransitionWidth=.005f,LowerTransitionBias=0};
Check("Sub-recommendation lower width remains effective",VirtualAxisMath.SurfaceWeight(t.Span,t.PelvicFloor+.003f*t.Span,t,p)>.5f);
var axis=new VirtualAxisMath.Axis(Vector3.Zero,Vector3.UnitY);
p.AxisPullLow=2;p.AxisPullHigh=-1;p.AxisPullAngle=40;
var result=VirtualAxisMath.Evaluate(axis,Matrix4x4.Identity,Matrix4x4.CreateRotationX(20*MathF.PI/180),p);
Check("Pull endpoints may exceed range and reverse ordering",Math.Abs(result.Pull-.5f)<1e-5f);
p.AxisPullLow=p.AxisPullHigh=1.5f;
Check("Pull strength greater than one is retained",VirtualAxisMath.Evaluate(axis,Matrix4x4.Identity,Matrix4x4.CreateRotationX(.2f),p).Pull==1.5f);
p=new VtxSettings{NavelStart=0};Check("Navel start below recommendation is used",BellyShape.NavelStageResponse(.1f,p)>0);
p.NavelPreviewFull=false;p.NavelStart=1;
Check("Zero response interval has finite endpoint",float.IsFinite(BellyShape.NavelStageResponse(1,p)));
var skin=new SkinSurfaceShading.Part {
    Original=new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(0,1,0)},
    Deformed=new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,1,.5f),new Vector3(0,1,-.4f)},
    Normals=Enumerable.Repeat(Vector3.UnitZ,4).ToArray(),Tangents=Enumerable.Repeat(new Vector4(1,0,0,1),4).ToArray(),
    Triangles=new[]{0,1,2,0,2,3},DetailProtection=new float[4]};
foreach(var pair in new[]{(-.5f,0f),(1.5f,1f)}){
    var a=SkinSurfaceShading.Apply(new[]{skin},pair.Item1).Parts[0];var b=SkinSurfaceShading.Apply(new[]{skin},pair.Item2).Parts[0];
    Check("Shading amount remains effective outside recommendation "+pair.Item1,a.Normals.Select((n,i)=>Vector3.Distance(n,b.Normals[i])).Max()>1e-5f&&a.Normals.All(n=>float.IsFinite(n.X+n.Y+n.Z)));
    Check("Unrestricted shading does not change geometry",a.Vertices.SequenceEqual(skin.Deformed));
}
Console.WriteLine($"Unrestricted input/kernel regression: {count} checks passed.");
