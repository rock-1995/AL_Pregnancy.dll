using ALPregnancy;
using System.Numerics;
using System.Text.Json;

internal static class CollisionRegression
{
    private static Vector3 V(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle());
    private static Vector3[] Points(JsonElement e)=>e.EnumerateArray().Select(V).ToArray();
    private static Matrix4x4 M(JsonElement e){var a=e.EnumerateArray().Select(x=>x.GetSingle()).ToArray();return new(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);}
    private static Matrix4x4 Skin(JsonElement weights,int i,Matrix4x4[] matrices)
    {var n=new Matrix4x4();for(int k=0;k<4;k++){float w=weights[i][k*2+1].GetSingle();if(w>0)n+=matrices[weights[i][k*2].GetInt32()]*w;}return n;}
    internal static void Run(string path,string output,Action<string,bool> check)
    {
        var surface=new CollisionSurfaceMap.Surface {Original=new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitY},Triangles=new[]{0,1,2},Affected=new[]{true,true,true}};
        var point=new Vector3(.2f,.3f,-.01f);var points=new[]{point,point,new Vector3(10,10,10)};
        var probes=CollisionSurfaceMap.Build(points,new[]{false,true,false},new[]{surface},.1f);
        check("Collision correspondence uses triangle barycentrics",probes[0].Valid&&Vector3.Distance(probes[0].Bary,new(.5f,.2f,.3f))<1e-6f);
        check("Excluded and distant collision vertices remain native",!probes[1].Valid&&!probes[2].Valid);
        var delta=new[]{new[]{Vector3.UnitZ,Vector3.UnitZ*2,Vector3.UnitZ*3}};
        check("Collision interpolation reproduces an affine displacement field",Vector3.Distance(probes[0].Sample(delta),Vector3.UnitZ*1.8f)<1e-6f);
        surface.Affected=new bool[3];check("Unaffected body surface does not acquire a collision patch",CollisionSurfaceMap.Build(new[]{point},null,new[]{surface},.1f).All(p=>!p.Valid));
        using var doc=JsonDocument.Parse(File.ReadAllText(path));var root=doc.RootElement;
        check("Physics replay uses the user's newly accepted 0.2.17 capture",root.GetProperty("version").GetString()=="0.2.17"&&root.GetProperty("settings").GetProperty("breastBoundaryRings").GetInt32()==4);
        var body=root.GetProperty("meshes").EnumerateArray().Where(m=>m.GetProperty("body").GetBoolean()&&m.GetProperty("active").GetBoolean()&&m.GetProperty("virtualBoneIndex").GetInt32()>0).ToArray();
        float span=root.GetProperty("anatomy").GetProperty("Span").GetSingle();
        var surfaces=new List<CollisionSurfaceMap.Surface>();var changes=new List<Vector3[]>();var nativeBody=new List<Vector3[]>();var finalBody=new List<Vector3[]>();
        foreach(var m in body)
        {
            var o=Points(m.GetProperty("vertices"));var d=Points(m.GetProperty("deformed"));var toRef=M(m.GetProperty("toReferenceRowMajor"));
            var skin=m.GetProperty("skinToWorldRowMajor").EnumerateArray().Select(M).ToArray();var mask=m.GetProperty("breastExcluded").EnumerateArray().Select(x=>x.GetBoolean()).ToArray();
            var native=new Vector3[o.Length];var final=new Vector3[o.Length];var disp=new Vector3[o.Length];
            for(int i=0;i<o.Length;i++)
            {
                native[i]=Vector3.Transform(o[i],Skin(m.GetProperty("originalSkinWeights"),i,skin));
                final[i]=Vector3.Transform(d[i],Skin(m.GetProperty("skinWeights"),i,skin));
                disp[i]=mask[i]?Vector3.Zero:final[i]-native[i];
            }
            nativeBody.Add(native);finalBody.Add(final);changes.Add(disp);
            surfaces.Add(new CollisionSurfaceMap.Surface {Original=o.Select(v=>Vector3.Transform(v,toRef)).ToArray(),Triangles=m.GetProperty("triangles").EnumerateArray().Select(x=>x.GetInt32()).ToArray(),Affected=disp.Select(v=>v.LengthSquared()>1e-12f).ToArray()});
        }
        var refNames=body[0].GetProperty("boneNames").EnumerateArray().Take(body[0].GetProperty("virtualBoneIndex").GetInt32()).Select(x=>x.GetString()!).ToArray();
        var refPoses=body[0].GetProperty("originalBindposesRowMajor").EnumerateArray().Select(M).ToArray();
        // Runtime reference is the selected torso, whereas source record maps are
        // to its coordinates. This capture's body pieces share the bind space.
        var evidence=new List<object>();int mapped=0,moved=0,frontMoved=0,improvedRays=0;float maxDelta=0,offsetError=0;bool finite=true,untouched=true,breasts=true;
        var dd=changes.ToArray();var ss=surfaces.ToArray();var timer=System.Diagnostics.Stopwatch.StartNew();
        foreach(var h in root.GetProperty("collision").GetProperty("helpers").EnumerateArray())
        {
            if(!h.GetProperty("active").GetBoolean())continue;
            var name=h.GetProperty("name").GetString()!;var original=Points(h.GetProperty("vertices"));var names=h.GetProperty("boneNames").EnumerateArray().Select(x=>x.GetString()!).ToArray();
            var bind=h.GetProperty("bindposesRowMajor").EnumerateArray().Select(M).ToArray();
            bool mapOk=RestSpaceMapping.TryCreate(names,bind,refNames,refPoses,out var map,out _,out _,out _)||RestSpaceMapping.TryCreateAliased(names,bind,refNames,refPoses,out map,out _,out _,out _);
            if(!mapOk){evidence.Add(new{name,mapOk});continue;}
            var weights=h.GetProperty("weights");var excluded=new bool[original.Length];
            for(int i=0;i<excluded.Length;i++)for(int k=0;k<4;k++)if(weights[i][k*2+1].GetSingle()>0&&names[weights[i][k*2].GetInt32()].Contains("bust",StringComparison.OrdinalIgnoreCase))excluded[i]=true;
            var probesActual=CollisionSurfaceMap.Build(original.Select(v=>Vector3.Transform(v,map)).ToArray(),excluded,ss,span*.35f);
            var bones=h.GetProperty("boneToWorldRowMajor").EnumerateArray().Select(M).ToArray();var matrices=bind.Select((b,i)=>b*bones[i]).ToArray();
            var renderer=M(h.GetProperty("rendererToWorldRowMajor"));Matrix4x4.Invert(renderer,out var inverse);
            var before=new Vector3[original.Length];var after=new Vector3[original.Length];int count=0;
            for(int i=0;i<original.Length;i++)
            {
                before[i]=Vector3.Transform(original[i],Skin(weights,i,matrices));
                var added=probesActual[i].Sample(dd);var local=Vector3.Transform(before[i],inverse)+Vector3.TransformNormal(added,inverse);after[i]=Vector3.Transform(local,renderer);
                finite &= float.IsFinite(after[i].LengthSquared());
                if(!probesActual[i].Valid)untouched &= added==Vector3.Zero;
                if(excluded[i])breasts &= added==Vector3.Zero;
                if(probesActual[i].Valid)
                {
                    mapped++;var q=probesActual[i];
                    Vector3 Sample(List<Vector3[]> list)=>list[q.Surface][q.A]*q.Bary.X+list[q.Surface][q.B]*q.Bary.Y+list[q.Surface][q.C]*q.Bary.Z;
                    var oldOffset=before[i]-Sample(nativeBody);var newOffset=after[i]-Sample(finalBody);offsetError=MathF.Max(offsetError,Vector3.Distance(oldOffset,newOffset));
                }
                if(added.LengthSquared()>1e-12f){count++;moved++;maxDelta=MathF.Max(maxDelta,added.Length());}
            }
            if(name is "o_hit_hara_f" or "o_hit_haraunder_f")frontMoved+=count;
            var triangles=h.GetProperty("triangles").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            for(int t=0;t<triangles.Length;t+=3)
            {
                int a=triangles[t],b=triangles[t+1],c=triangles[t+2];var oldCenter=(before[a]+before[b]+before[c])/3;var newCenter=(after[a]+after[b]+after[c])/3;
                var direction=newCenter-oldCenter;if(direction.Length()<.02f)continue;direction=Vector3.Normalize(direction);
                var origin=newCenter+direction*.05f;
                float oldHit=RayMesh(origin,-direction,before,triangles),newHit=RayMesh(origin,-direction,after,triangles);
                if(float.IsFinite(newHit)&&newHit+.01f<oldHit)improvedRays++;
            }
            evidence.Add(new{name,mapOk,vertices=original.Length,mapped=probesActual.Count(p=>p.Valid),moved=count});
        }
        check("Real abdomen collision proxies receive the final body displacement",mapped>20&&frontMoved>15&&maxDelta>.03f);
        check("Collision update keeps every excluded or unmapped point native",untouched&&breasts&&finite);
        check("Native animated body-to-collider offset survives deformation",offsetError<2e-5f);
        check("Particle ray samples hit the expanded belly before the old body",improvedRays>5);
        var settings=JsonSerializer.Deserialize<VtxSettings>(root.GetProperty("settings").GetRawText())!;
        // The historical pose keeps its original settings; retired fields are ignored and new trial settings use their defaults.
        var historicalDefaults = new VtxSettings { UpperFoldSupport = 1f };
        check("Historical defaults match the accepted 0.2.17 capture after migration of retired fields and new trial defaults",JsonSerializer.Serialize(historicalDefaults)==JsonSerializer.Serialize(settings));
        File.WriteAllText(output,JsonSerializer.Serialize(new{sourceVersion="0.2.17",span,mapped,moved,frontMoved,maxDelta,offsetError,improvedRays,elapsedMs=timer.Elapsed.TotalMilliseconds,helpers=evidence,note="Offline geometry and ray tests; not a live Obi simulation"},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"COLLISION: mapped={mapped}, moved={moved}, abdomen={frontMoved}, maxDelta={maxDelta:F5}, offsetError={offsetError:G4}, earlierHits={improvedRays}");
    }
    private static float RayMesh(Vector3 origin,Vector3 direction,Vector3[] v,int[] tr)
    {
        float result=float.PositiveInfinity;
        for(int t=0;t<tr.Length;t+=3)
        {
            var a=v[tr[t]];var e1=v[tr[t+1]]-a;var e2=v[tr[t+2]]-a;var h=Vector3.Cross(direction,e2);float det=Vector3.Dot(e1,h);if(MathF.Abs(det)<1e-9f)continue;
            var s=origin-a;float u=Vector3.Dot(s,h)/det;if(u<0||u>1)continue;var q=Vector3.Cross(s,e1);float w=Vector3.Dot(direction,q)/det;if(w<0||u+w>1)continue;
            float hit=Vector3.Dot(e2,q)/det;if(hit>=0)result=MathF.Min(result,hit);
        }
        return result;
    }
}
