using UnityEngine;
namespace ALPregnancy;
internal static partial class FastGeometry
{
 private static void RuntimeLogInfo(string text) { }
 private static class Log { internal static void LogWarning(string text)=>throw new Exception(text); }
 internal static int[] SearchCases(Vector3[] vertices,bool[] affected,Vector3[] queries)
 {
  var c=new BodyAnchorContext{CellSize=1};
  c.Meshes.Add(new BodyAnchorMesh{Original=vertices,Affected=affected});
  for(int i=0;i<vertices.Length;i++)c.Points.Add(new(){Original=vertices[i],OriginalUp=vertices[i].y,MeshIndex=0,VertexIndex=i});
  FinalizeBodyAnchorContext(c);
  var results=new List<int>();
  foreach(var p in queries)foreach(bool require in new[]{false,true})results.Add(TryFindNearestBodyAnchorPoint(c,p,require,out var n)?n.VertexIndex:-1);
  return results.ToArray();
 }
 internal static float[] Replay(Vector3[] vertices,int[] triangles,Vector3[] cloth,float stage)
 {
  var results=new List<float>();
  var profile=new TorsoProfile{Pubis=-1,Ribs=1,Navel=0};
  var frame=new LocalFrame {Profile=profile,BoneLen=.3f};
  var mesh=new BodyAnchorMesh{Original=vertices,Morphed=new Vector3[vertices.Length],Valid=new bool[vertices.Length],Affected=new bool[vertices.Length],SurfaceTrianglesByVertex=new List<int>[vertices.Length]};
  var context=new BodyAnchorContext {CellSize=.075f};context.Meshes.Add(mesh);
  for(int i=0;i<vertices.Length;i++) {
   var p=vertices[i];float push=stage*.5f*MathF.Exp(-(p.x*p.x+p.y*p.y)*4);
   mesh.Morphed[i]=new(){x=p.x,y=p.y-stage*.08f,z=p.z+push};mesh.Valid[i]=true;mesh.Affected[i]=push>.0002f;
   context.Points.Add(new BodyAnchorPoint{MeshIndex=0,VertexIndex=i,Original=p,OriginalUp=p.y});
   mesh.SurfaceTrianglesByVertex[i]=new();
  }
  for(int i=0;i<triangles.Length;i+=3) {
   int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
   if(!mesh.Affected[a]&&!mesh.Affected[b]&&!mesh.Affected[c])continue;
   int t=context.SurfaceTriangles.Count;context.SurfaceTriangles.Add(new(){MeshIndex=0,A=a,B=b,C=c});
   mesh.SurfaceTrianglesByVertex[a].Add(t);mesh.SurfaceTrianglesByVertex[b].Add(t);mesh.SurfaceTrianglesByVertex[c].Add(t);
  }
  FinalizeBodyAnchorContext(context);
  foreach(var p in cloth) {
   bool found=TryFindNearestBodyAnchorPoint(context,p,false,out var near);results.Add(found?near.VertexIndex:-1);
   found=TryFindNearestBodyAnchorPoint(context,p,true,out near);results.Add(found?near.VertexIndex:-1);
   found=TryGetBodySurfaceClothTarget(context,p,1,frame,out var target);results.Add(found?1:0);results.Add(target.x);results.Add(target.y);results.Add(target.z);
  }
  var welded=ComputeNormalWeldGroup(vertices);
  var normals=ComputeWeldedNormalsOffline(new Mesh{triangles=triangles},mesh.Morphed,welded);
  foreach(var n in normals){results.Add(n.x);results.Add(n.y);results.Add(n.z);}
  foreach(var i in BuildNearestBodyVertCache(cloth.Take(100).ToArray(),vertices))results.Add(i);
  var neighbors=Enumerable.Range(0,vertices.Length).Select(_=>new List<int>()).ToArray();
  for(int i=0;i<triangles.Length;i+=3)for(int j=0;j<3;j++)for(int k=0;k<3;k++)if(j!=k&&!neighbors[triangles[i+j]].Contains(triangles[i+k]))neighbors[triangles[i+j]].Add(triangles[i+k]);
  mesh.Morphed[vertices.Length/2].z+=1;
  results.Add(RepairClothDistortion(vertices,mesh.Morphed,mesh.Affected,frame,neighbors));
  foreach(var p in mesh.Morphed){results.Add(p.x);results.Add(p.y);results.Add(p.z);}
  return results.ToArray();
 }
}
