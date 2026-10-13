using UnityEngine;
using Character;
using ALPregnancy;
int checks=0;
void Check(string s,bool ok){checks++;if(!ok)throw new Exception(s);Console.WriteLine("PASS "+s);}
Transform T(int id,string name,Transform parent=null)=>new(){Id=id,name=name,parent=parent};
var chest=T(1,"cf_s_spine03");var sternum=T(2,"cf_s_bust00_C",chest);
var leftRoot=T(3,"physics_driver_left",chest);var rightRoot=T(4,"physics_driver_right",chest);
var leftSkin=T(5,"cf_s_bust02_L",leftRoot);var helper=T(6,"shape_scale",rightRoot);var rightSkin=T(7,"unrelated_skin_name",helper);
var nipple=T(8,"cf_s_bnip02_tip_L",leftSkin);var unrelated=T(9,"cf_s_bust01_cosmetic",chest);
var hipRoot=T(10,"hip_driver",chest);var hipSkin=T(11,"hip_skin",hipRoot);
HumanMagicaSpringSetting.BoneData Entry(params Transform[] roots)=>new(){Spring=new(){SerializeData=new(){rootBones=roots.ToList()}}};
var h=new Human{Body=new(){magicaSpringSetting=new(){Bust=new[]{Entry(leftRoot,rightRoot),Entry(leftRoot),null},Hip=new[]{Entry(hipRoot)}}}};
var bones=new[]{chest,sternum,leftSkin,rightSkin,nipple,unrelated,hipSkin};
var mesh=new Mesh{boneWeights=Enumerable.Range(0,bones.Length).Select(i=>new BoneWeight{boneIndex0=i,weight0=1}).Concat(new[]{
    new BoneWeight{boneIndex0=2,weight0=.1f,boneIndex1=1,weight1=.6f,boneIndex2=3,weight2=.2f,boneIndex3=6,weight3=.1f},
    new BoneWeight{boneIndex0=4,weight0=.00001f,boneIndex1=0,weight1=.99999f}
}).ToArray()};
var renderer=new SkinnedMeshRenderer{bones=bones};var weights=BellyVertexMorph.TestWeights(h,mesh,renderer);
Check("Chest and sternum weights are not breast physics",weights[0]==0&&weights[1]==0);
Check("Both physics chains include descendant skin helpers",weights[2]==1&&weights[3]==1);
Check("Nipple chain is included even without breast keywords",weights[4]==1);
Check("Bust keyword outside physics chain is not protected",weights[5]==0);
Check("Hip physics is not misclassified as breast physics",weights[6]==0);
Check("All four native weights contribute by physics ownership",Math.Abs(weights[7]-.3f)<1e-6f);
Check("Tiny real physics weights survive without an invented threshold",weights[8]==.00001f);
var mask=BreastExclusion.Build(weights,weights.Length,null,true);
Check("Exclusion consumes physics map and leaves sternum deformable",!mask[0]&&!mask[1]&&mask[2]&&mask[3]&&mask[4]&&!mask[5]&&!mask[6]&&mask[7]&&mask[8]);
Check("Existing OFF setting remains OFF",BreastExclusion.Build(weights,weights.Length,null,false).All(x=>!x));
Check("Diagnostics name both actual root paths",BellyVertexMorph.TestPaths(h).Length==2);
// IL2CPP may return another wrapper for the same Transform; compare native identity.
helper.parent=T(4,"physics_driver_right",chest);
Check("Different wrappers of the same native root are recognized",BellyVertexMorph.TestWeights(h,mesh,renderer)[3]==1);
h.Body.magicaSpringSetting.Bust=Array.Empty<HumanMagicaSpringSetting.BoneData>();
Check("Missing physics group never falls back to guessed bone names",BellyVertexMorph.TestWeights(h,mesh,renderer).All(x=>x==0));
Check("Null actor has no physics ownership",BellyVertexMorph.TestWeights(null,mesh,renderer).All(x=>x==0));
AxisDriverRegression.Run(Check);
if(args.Length > 0) AssetReplay.Run(Check, args[0]);
Console.WriteLine($"PASS {checks} breast physics and axis driver runtime checks");
