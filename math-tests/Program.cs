using ALPregnancy;
using UnityEngine;
using System.Diagnostics;
using System.Text.Json;

var rng=new Random(11324);var vertices=new List<Vector3>();var triangles=new List<int>();
const int side=49;
for(int y=0;y<side;y++)for(int x=0;x<side;x++){float xx=(x-side/2)*.04f,yy=(y-side/2)*.04f;vertices.Add(new(xx,yy,.1f*MathF.Cos(xx*2)*MathF.Cos(yy*2)));}
for(int y=0;y<side-1;y++)for(int x=0;x<side-1;x++){int i=y*side+x;triangles.AddRange(new[]{i,i+1,i+side,i+1,i+side+1,i+side});}
var cloth=Enumerable.Range(0,1200).Select(i=>new Vector3((float)rng.NextDouble()*2.6f-1.3f,(float)rng.NextDouble()*2.6f-1.3f,(float)rng.NextDouble()*.2f)).ToArray();
var data=vertices.ToArray();var tris=triangles.ToArray();var report=new List<object>();long checks=0;
void Search(Vector3[] points,bool[] affected,Vector3[] queries) {
 var old=LegacyGeometry.SearchCases(points,affected,queries);var current=FastGeometry.SearchCases(points,affected,queries);
 if(!old.SequenceEqual(current))throw new Exception("Nearest index changed legacy selection/tie/fallback");checks+=old.Length;
}
for(int trial=0;trial<80;trial++) {
 var points=Enumerable.Range(0,trial*7%401).Select(i=>new Vector3((float)rng.NextDouble()*12-6,(float)rng.NextDouble()*12-6,(float)rng.NextDouble()*12-6)).ToArray();
 var queries=Enumerable.Range(0,100).Select(i=>new Vector3((float)rng.NextDouble()*40-20,(float)rng.NextDouble()*40-20,(float)rng.NextDouble()*40-20)).ToArray();
 Search(points,points.Select((_,i)=>i%(trial%5+1)==0).ToArray(),queries);
}
Search(new[]{new Vector3(-1,0,0),new Vector3(1,0,0),new Vector3(-1,0,0)},new[]{true,true,false},new[]{new Vector3(0,0,0),new Vector3(0,40,0)});
// Nearby hashed bucket aliases far-away coordinates; legacy intentionally picks
// that local-bucket member even when a globally nearer nonmember exists.
Search(new[]{new Vector3(1,-31,0),new Vector3(0,0,5),new Vector3(0,1,-31)},new[]{true,true,true},new[]{new Vector3(0,0,0),new Vector3(0,0,1)});
Search(new[]{new Vector3(float.NaN,0,0),new Vector3(1,0,0)},new[]{true,false},new[]{new Vector3(0,0,0),new Vector3(float.NaN,0,0)});
Search(new[]{new Vector3(1,0,0),new Vector3(2,0,0)},new[]{true,false},new[]{new Vector3(float.PositiveInfinity,0,0)});
foreach(float stage in new[]{0f,.01f,.1f,.33f,.55f,.8f,1f,.8f,.33f,.01f,0f}) {
 Probe.Calls=0;var old=LegacyGeometry.Replay(data,tris,cloth,stage);long oldCalls=Probe.Calls;
 Probe.Calls=0;var sw=Stopwatch.StartNew();var current=FastGeometry.Replay(data,tris,cloth,stage);sw.Stop();long calls=Probe.Calls;
 if(old.Length!=current.Length)throw new Exception("Output length changed");
 for(int i=0;i<old.Length;i++){checks++;if(BitConverter.SingleToInt32Bits(old[i])!=BitConverter.SingleToInt32Bits(current[i]))throw new Exception($"stage={stage} value={i}: {old[i]:R} != {current[i]:R}");}
 if(calls!=0)throw new Exception($"Hot geometry still calls {calls} native math wrappers");
 report.Add(new{Stage=stage,Results=old.Length,LegacyWrapperCalls=oldCalls,FastWrapperCalls=calls,ManagedReplayMs=sw.Elapsed.TotalMilliseconds});
}
// Boundary semantics used by welding, normals and slider interpolation.
foreach(float v in new[]{-2.5f,-1.5f,-.5f,-0f,.5f,1.5f,2.5f,float.NaN,float.PositiveInfinity,float.NegativeInfinity}) {
 if(MorphMath.RoundToInt(v)!=Mathf.RoundToInt(v))throw new Exception("Rounding changed");
 if(!MorphMath.Clamp01(v).Equals(Mathf.Clamp01(v)))throw new Exception("Clamping changed");
}
foreach(float v in new[]{0,1e-6f,1e-5f,1.00001e-5f,1}){var p=new Vector3(v,0,0);var a=MorphMath.Normalize(p);var b=p.normalized;if(!a.x.Equals(b.x))throw new Exception("Normalization threshold changed");}
Console.WriteLine($"PASS {checks} bitwise geometry comparisons; 11 forward/reverse stages; all replay hot math calls removed.");
Console.WriteLine(JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("Synthetic offline replay uses independent Unity-style scalar stubs; timing excludes native mesh upload, game scene loading, and rendered FPS.");
namespace ALPregnancy { internal static class BellyDeformSettings { internal static VtxSettings Vtx=new(); } }
