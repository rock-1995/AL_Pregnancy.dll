using ALPregnancy;
using Character;
using UnityEngine;

int passed=0;
void Check(string label,bool success) { if(!success)throw new Exception("FAIL: "+label);passed++;Console.WriteLine("PASS: "+label); }
bool Near(double a,double b)=>Math.Abs(a-b)<.000001;
Check("first pregnancy halfway is 0.1 at x=5",Near(BodyChangeMath.Progress(true,.5,0,5),.1));
Check("two births plus half pregnancy is 0.5",Near(BodyChangeMath.Progress(true,.5,2,5),.5));
Check("postpartum retains two fifths",Near(BodyChangeMath.Progress(false,0,2,5),.4));
Check("five births gives exactly full delta",BodyChangeMath.Progress(false,0,5,5)==1);
Check("more births and pregnancy do not exceed full delta",BodyChangeMath.Progress(true,.9,20,5)==1);
Check("x=0 and negative x never divide by zero",BodyChangeMath.Progress(true,0,0,0)==1&&BodyChangeMath.Progress(false,0,0,-5)==1);
Check("int maximum birth count saturates without overflow",BodyChangeMath.Progress(true,1,int.MaxValue,int.MaxValue)==1);
Check("int maximum x preserves a small fractional change",BodyChangeMath.Progress(true,1,0,int.MaxValue)>0);
Check("upper clamp applies after additive change",Near(BodyChangeMath.Add(.9f,.4f,.5,0,100),1));
Check("lower clamp applies after negative change",BodyChangeMath.Add(.2f,-.5f,1,0,100)==0);
Check("negative and above-100 limits are respected",Near(BodyChangeMath.Add(.9f,.4f,1,-200,300),1.3)&&Near(BodyChangeMath.Add(.2f,-.5f,1,-200,300),-.3));
Check("reversed bounds use ordered range",Near(BodyChangeMath.Add(.9f,.4f,1,150,-50),1.3));
Check("unaffected values outside range remain untouched",BodyChangeMath.Add(2,0,1,0,100)==2);
Check("zero progress does not clamp originals",BodyChangeMath.Add(-1,1,0,0,100)==-1);
Check("color caps rather than scales early progress",Near(BodyChangeMath.ColorProgress(.5,.85f),.5));
Check("color caps full progress at 0.85",Near(BodyChangeMath.ColorProgress(1,.85f),.85));
Check("color maximum zero turns color change off",BodyChangeMath.ColorProgress(1,0)==0);

var baseline=BodyChangeValues.Read(HumanData.LoadFixture("base"));
var reference=BodyChangeValues.Read(HumanData.LoadFixture("reference"));
var delta=BodyChangeValues.Difference(baseline,reference);
Check("actual cards have 30 changed shape sliders",delta.Shape.Count(x=>x!=0)==30);
Check("actual cards have eight changed extra morph controls",delta.Extra.Count(x=>x!=0)==8);
Check("actual card color difference is negative RGB with unchanged alpha",delta.Nipple.r<0&&delta.Nipple.g<0&&delta.Nipple.b<0&&delta.Nipple.a==0);
var complete=baseline.Apply(delta,1,0,100,1);
Check("base plus full difference reconstructs T body and color",complete.Same(reference));
var capped=baseline.Apply(delta,1,0,100,.85f);
Check("default completed color retains 15 percent of base",Near(capped.Nipple.r,baseline.Nipple.r*.15)&&Near(capped.Nipple.g,baseline.Nipple.g*.15)&&Near(capped.Nipple.b,baseline.Nipple.b*.15));
Check("color cap does not reduce full body changes",capped.Shape.SequenceEqual(reference.Shape)&&capped.Extra.SequenceEqual(reference.Extra));
var changedBody=HumanData.LoadFixture("base");changedBody.shapeValueBody[3]=.4f;
var different=BodyChangeValues.Read(changedBody).Apply(delta,.5,0,100,.85f);
Check("each character receives an additive delta from its own baseline",Near(different.Shape[3],.4+delta.Shape[3]*.5));
Check("face and unrelated body IDs are not part of transfer",changedBody.skinId==17&&different.Shape[0]==baseline.Shape[0]);

// Run production adapter with managed native API doubles; no game process/files.
File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"base.png"),"native-loader fixture");
File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"reference.png"),"native-loader fixture");
var human=new Human();human.Data.Custom.Body=HumanData.LoadFixture("base");
var originalBody=human.Data.Custom.Body;
var originalArray=originalBody.shapeValueBody;
var pregnancy=new PregnancyRecord {Pregnant=true,PregnancyProgress=.5};
void Tick()
{
    human.Body.LateUpdate();
    BodyChangeRuntime.ApplyAfterBelly(human,pregnancy);
}
Tick();var first=human.Body.Applied;
Check("post-belly native shape commit observes changed values",Near(first.Shape[3],baseline.Shape[3]+delta.Shape[3]*.1));
Check("original body and array references restored after tick",ReferenceEquals(originalBody,human.Data.Custom.Body)&&ReferenceEquals(originalArray,originalBody.shapeValueBody));
Check("original data contents preserved despite shallow native Copy",BodyChangeValues.Read(originalBody).Same(baseline));
Check("all eight native auxiliary setters read the temporary body",human.Body.AppliedExtras.SequenceEqual(first.Extra));
Check("native color setter reads the temporary color",human.Body.AppliedNipple==first.Nipple);
Check("first post-belly commit applies body values and shape exactly once",human.Body.ValueUpdates==1&&human.Body.ShapeUpdates==1);
int initialReleases=PregnancyRuntime.Releases;
int initialExtras=human.Body.ExtraCalls;
for(int i=0;i<100;i++)Tick();
Check("one hundred frames never accumulate the offset",human.Body.Applied.Same(first));
Check("unchanged frames do not repeatedly rebuild belly mesh",PregnancyRuntime.Releases==initialReleases);
Check("one hundred stable frames do not force native shape updates",human.Body.ValueUpdates==1&&human.Body.ShapeUpdates==1);
Check("one hundred stable frames do not call native auxiliary setters",human.Body.ExtraCalls==initialExtras);
human.Body.SetUpdateShapeBodyValue();human.Body.SetUpdateShapeBody();Tick();
Check("game-requested base refresh is followed by target once after belly",human.Body.Applied.Same(first)&&human.Body.ValueUpdates==3&&human.Body.ShapeUpdates==3);
originalBody.skinId=23;
var liveUnrelated=BodyChangeRuntime.BeginAfterBelly(human,pregnancy);
Check("temporary body preserves current unrelated data",human.Data.Custom.Body.skinId==23);
BodyChangeRuntime.End(human,liveUnrelated);
var outer=BodyChangeRuntime.BeginAfterBelly(human,pregnancy);
var nested=BodyChangeRuntime.BeginAfterBelly(human,pregnancy);
Check("reentrant native call does not add twice",nested==null&&BodyChangeValues.Read(human.Data.Custom.Body).Same(first));
BodyChangeRuntime.End(human,nested);BodyChangeRuntime.End(human,outer);
Check("outer native scope survives nested finalizer",ReferenceEquals(human.Data.Custom.Body,originalBody));

pregnancy.PregnancyProgress=1;Tick();var beforeBirth=human.Body.Applied;
pregnancy.Pregnant=false;pregnancy.PregnancyProgress=0;pregnancy.Births=1;Tick();
Check("birth preserves body change continuously",human.Body.Applied.Same(beforeBirth));
pregnancy.Births=5;Tick();Check("five births keep full morphology postpartum",human.Body.Applied.Same(capped));
PregnancyConfig.BodyBirths.Value=10;Tick();
Check("live x changes recalculate using original data",human.Body.Applied.Same(baseline.Apply(delta,.5,0,100,.85f)));
PregnancyConfig.NippleColorMaximum.Value=0;Tick();
Check("live zero color cap restores original color only",human.Body.Applied.Nipple==baseline.Nipple&&human.Body.Applied.Shape[3]!=baseline.Shape[3]);
PregnancyConfig.NippleColorMaximum.Value=.85f;PregnancyConfig.BodyBirths.Value=5;
PregnancyConfig.BodyLower.Value=-50;PregnancyConfig.BodyUpper.Value=150;originalBody.shapeValueBody[3]=1.2f;Tick();
Check("changed live source baseline and extended bounds are respected",Near(human.Body.Applied.Shape[3],1.2+delta.Shape[3]));
PregnancyPlugin.Enabled.Value=false;Tick();
Check("disabling restores original rendered values",human.Body.Applied.Same(BodyChangeValues.Read(originalBody)));
Check("disabling restores all native auxiliary values and color",human.Body.AppliedExtras.SequenceEqual(BodyChangeValues.Read(originalBody).Extra)&&human.Body.AppliedNipple==originalBody.nipColor);
PregnancyPlugin.Enabled.Value=true;Tick();
BodyChangeRuntime.ReleaseAll();
Check("world change restores original rendered values",human.Body.Applied.Same(BodyChangeValues.Read(originalBody)));
Tick();BodyChangeRuntime.Release(human);human.Body=new HumanBody(human);Tick();
Check("body reload reapplies color and auxiliary settings",human.Body.ExtraCalls==9);
BoundaryOrderRegression.Run(Check);
AnimationContactRegression.Run(Check);
BodyChangeRuntime.ReleaseAll();human.Body.ThrowExtra=true;
try {BodyChangeRuntime.BeginAfterBelly(human,pregnancy);throw new Exception("expected failure");}catch(InvalidOperationException){}
Check("native preparation failure cannot leak temporary data",ReferenceEquals(human.Data.Custom.Body,originalBody));
human.Body.ThrowExtra=false;
var failed=BodyChangeRuntime.BeginAfterBelly(human,pregnancy);
try {throw new InvalidOperationException("native LateUpdate failure");}catch(InvalidOperationException){}
finally {BodyChangeRuntime.End(human,failed);BodyChangeRuntime.End(human,failed);}
Check("native failure restoration is idempotent",ReferenceEquals(human.Data.Custom.Body,originalBody));
BodyChangeRuntime.ReleaseAll();
Tick();File.Delete(Path.Combine(AppContext.BaseDirectory,"reference.png"));Time.unscaledTime=3;
Tick();Check("missing replacement card restores original instead of stale changes",human.Body.Applied.Same(BodyChangeValues.Read(originalBody)));
Console.WriteLine($"Body change regression: {passed} checks passed.");
