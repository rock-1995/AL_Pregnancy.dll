using UnityEngine;
using System.Text.Json;
using NVector=System.Numerics.Vector3;
using NMatrix=System.Numerics.Matrix4x4;
namespace ALPregnancy;
internal static partial class BellyVertexMorph
{
    static NVector V(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle());
    static NVector[] Points(JsonElement e)=>e.EnumerateArray().Select(V).ToArray();
    static NMatrix M(JsonElement e){var a=e.EnumerateArray().Select(x=>x.GetSingle()).ToArray();return new(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);}
    static BoneWeight W(JsonElement e)=>new(){boneIndex0=e[0].GetInt32(),weight0=e[1].GetSingle(),boneIndex1=e[2].GetInt32(),weight1=e[3].GetSingle(),boneIndex2=e[4].GetInt32(),weight2=e[5].GetSingle(),boneIndex3=e[6].GetInt32(),weight3=e[7].GetSingle()};
    static NMatrix Skin(BoneWeight w,NMatrix[] matrices){var m=new NMatrix();foreach(var x in Weights(w))if(x.Weight>0)m+=matrices[x.Bone]*x.Weight;return m;}
    static void Replay(string input,string output)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(input));var root=doc.RootElement;
        Check("Offline replay identifies its historical source version",root.GetProperty("version").GetString()=="0.2.19");
        var settings=JsonSerializer.Deserialize<VtxSettings>(root.GetProperty("settings").GetRawText());BellyDeformSettings.Vtx=settings;
        var profile=JsonSerializer.Deserialize<TorsoProfile>(root.GetProperty("anatomy").GetRawText());
        foreach(string key in new[]{"SkinNavelY","SkinNavelZ"})if(root.GetProperty("anatomy").TryGetProperty(key,out var val)&&val.ValueKind==JsonValueKind.Number)typeof(TorsoProfile).GetProperty(key).SetValue(profile,val.GetSingle());
        var f=root.GetProperty("frame");var center=V(f.GetProperty("center"));var up=V(f.GetProperty("up"));var right=V(f.GetProperty("right"));var forward=V(f.GetProperty("forward"));
        var frame=new NMatrix(right.X,right.Y,right.Z,0,up.X,up.Y,up.Z,0,forward.X,forward.Y,forward.Z,0,center.X,center.Y,center.Z,1);NMatrix.Invert(frame,out var inverseFrame);
        float stage=f.GetProperty("rate").GetSingle();var virtualWorld=M(root.GetProperty("virtualAxis").GetProperty("transform"));
        var body=root.GetProperty("meshes").EnumerateArray().Where(m=>m.GetProperty("body").GetBoolean()&&m.GetProperty("active").GetBoolean()&&m.GetProperty("virtualBoneIndex").GetInt32()>0).ToArray();
        var refNames=body[0].GetProperty("boneNames").EnumerateArray().Take(body[0].GetProperty("virtualBoneIndex").GetInt32()).Select(x=>x.GetString()).ToArray();
        var refBinds=body[0].GetProperty("originalBindposesRowMajor").EnumerateArray().Select(M).ToArray();
        var refSkin=body[0].GetProperty("skinToWorldRowMajor").EnumerateArray().Take(refNames.Length).Select(M).ToArray();
        var refBones=refNames.Select((name,i)=>{NMatrix.Invert(refBinds[i],out var inv);return new Transform{name=name,localToWorldMatrix=U(inv*refSkin[i])};}).ToArray();
        int pi=Array.IndexOf(refNames,"cf_s_waist01");var pelvis=refSkin[pi];var frameWorld=frame*pelvis;NMatrix.Invert(frameWorld,out var inverseWorld);
        float maxStaticError=0,maxBlendError=0;int baselineVertices=0;
        NVector bodyPairOriginal=default,bodyPairStatic=default,collisionPairOriginal=default,collisionPairStatic=default;
        foreach(var bodyMesh in body)
        {
            var o=Points(bodyMesh.GetProperty("vertices"));var baseline=Points(bodyMesh.GetProperty("staticDeformed"));var toRef=M(bodyMesh.GetProperty("toReferenceRowMajor"));
            var material=o.Select(x=>NVector.Transform(x,toRef*inverseFrame)/profile.Span).ToArray();
            var tr=bodyMesh.GetProperty("triangles").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            var breast=bodyMesh.GetProperty("breastWeights");
            var excluded=BreastExclusion.Build(breast.ValueKind==JsonValueKind.Array ? breast.EnumerateArray().Select(x=>x.GetSingle()).ToArray() : null,o.Length,null,settings.BreastExclusionEnabled);var influence=bodyMesh.GetProperty("influence").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            var build=CollisionDeformation.Build(o,toRef,frame,profile,stage,settings,influence,excluded,true,null);
            if(bodyMesh.GetProperty("mesh").GetString()=="o_lower_type04")
            { bodyPairOriginal=NVector.Transform(o[1562],toRef*inverseFrame);bodyPairStatic=NVector.Transform(build.Static[1562],toRef*inverseFrame); }
            var blend=bodyMesh.GetProperty("virtualBlend").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            for(int i=0;i<o.Length;i++){maxStaticError=MathF.Max(maxStaticError,NVector.Distance(build.Static[i],baseline[i]));maxBlendError=MathF.Max(maxBlendError,MathF.Abs(build.Blend[i]-blend[i]));}
            baselineVertices+=o.Length;
            var skin=bodyMesh.GetProperty("skinToWorldRowMajor").EnumerateArray().Select(M).ToArray();var weights=bodyMesh.GetProperty("originalSkinWeights").EnumerateArray().Select(W).ToArray();
            NMatrix.Invert(toRef,out var fromRef);
            float bodyError=0,blendError=0;
            for(int i=0;i<o.Length;i++)
            {
                var local=NVector.Transform(o[i],toRef*inverseFrame);
                float effective=BellyShape.DeformationInfluence(influence[i],local.Y,profile,settings);
                var expected=o[i]+NVector.TransformNormal((BellyShape.Deform(local,profile,stage,settings)-local)*effective,frame*fromRef);
                float expectedBlend=VirtualAxisMath.MaterialSurfaceWeight(NVector.TransformNormal(expected-o[i],toRef).Length(),local,profile,stage,settings,effective);
                bodyError=MathF.Max(bodyError,NVector.Distance(build.Static[i],expected));
                blendError=MathF.Max(blendError,MathF.Abs(build.Blend[i]-expectedBlend));
            }
            Check("Collision kernel agrees with current display formula on "+bodyMesh.GetProperty("mesh").GetString(),bodyError<2e-5f&&blendError<2e-5f);
            var reverse=CollisionDeformation.Build(o.Reverse().ToArray(),toRef,frame,profile,stage,settings,influence.Reverse().ToArray(),excluded.Reverse().ToArray(),true,null);
            Check("Rest deformation and attachment are independent of vertex order",reverse.Static.Reverse().SequenceEqual(build.Static)&&reverse.Blend.Reverse().SequenceEqual(build.Blend));
        }
        var referenceMesh=new Mesh{bindposes=refBinds.Select(U).ToArray()};var reference=new SkinnedMeshRenderer{sharedMesh=referenceMesh,bones=refBones};
        var axis=new VirtualRig{Pelvis=refBones[pi],PelvisBind=refBinds[pi],Last=new(virtualWorld,0,0,default,default,default)};
        var human=new Character.Human{Pointer=new IntPtr(987654)};var state=new CharaState{HumanPtr=human.Pointer,Profile=profile,SMR=reference,Frame=frame,LastAppliedRate=stage,Virtual=axis};
        var helpers=new List<SkinnedCollisionHelper>();var originals=new Dictionary<int,Vector3[]>();var native=new Dictionary<int,Vector3[]>();
        foreach(var h in root.GetProperty("collision").GetProperty("helpers").EnumerateArray())
        {
            if(!h.GetProperty("active").GetBoolean())continue;
            var mesh=new Mesh{name=h.GetProperty("sourceMesh").GetString(),vertices=Points(h.GetProperty("vertices")).Select(ToUnity).ToArray(),triangles=h.GetProperty("triangles").EnumerateArray().Select(x=>x.GetInt32()).ToArray(),
                bindposes=h.GetProperty("bindposesRowMajor").EnumerateArray().Select(x=>U(M(x))).ToArray(),boneWeights=h.GetProperty("weights").EnumerateArray().Select(W).ToArray()};
            var names=h.GetProperty("boneNames").EnumerateArray().Select(x=>x.GetString()).ToArray();var boneWorld=h.GetProperty("boneToWorldRowMajor").EnumerateArray().Select(M).ToArray();
            var renderer=new SkinnedMeshRenderer{sharedMesh=mesh,bones=names.Select((n,i)=>new Transform{name=n,localToWorldMatrix=U(boneWorld[i])}).ToArray()};
            var helper=Helper(renderer);helper.name=h.GetProperty("name").GetString();helper.transform.localToWorldMatrix=U(M(h.GetProperty("rendererToWorldRowMajor")));
            helper.UpdateCollisionMesh();helpers.Add(helper);originals[helper.GetInstanceID()]=mesh.vertices;native[helper.GetInstanceID()]=helper._meshCalc.vertices;
        }
        human.GameObject.Components=helpers.ToArray();_state[987654]=state;_records[987654]=new();UpdateBodyCollision(human);
        Check("Production preparation builds low-poly native caches from the real capture",state.Collision?.Ready==true&&state.Collision.Bindings.Length>=2&&!state.Collision.Failed);
        bool untouched=true,finite=true,sourceExact=true,cacheExact=true;int moved=0,front=0,upper=0,earlierHits=0;float maxDistance=0;
        var details=new List<object>();
        foreach(var helper in helpers)
        {
            int id=helper.GetInstanceID();var after=helper._meshCalc.vertices;var old=native[id];int count=0;
            CollisionBindings.TryGetValue(id,out var b);var mask=b==null?new HashSet<int>():b.Shape.Indices.ToHashSet();
            if(helper.name=="o_hit_hara_f"&&b!=null)
            { collisionPairOriginal=NVector.Transform(b.Shape.Original[27],b.ToReference*inverseFrame);collisionPairStatic=NVector.Transform(b.Shape.Static[27],b.ToReference*inverseFrame); }
            for(int i=0;i<after.Length;i++)
            {
                float d=NVector.Distance(new(after[i].x,after[i].y,after[i].z),new(old[i].x,old[i].y,old[i].z));
                finite&=float.IsFinite(d);if(!mask.Contains(i))untouched&=d<1e-6f;
                if(d>1e-5f){count++;moved++;maxDistance=MathF.Max(maxDistance,d);}
                if(b!=null&&b.Shape.Support[i].LengthSquared()>1e-14f)upper++;
            }
            if(helper.name is "o_hit_hara_f" or "o_hit_haraunder_f")front+=count;
            var oldPoints=ToManagedVectors(old);var newPoints=ToManagedVectors(after);var tr=helper._meshCalc.triangles;
            for(int t=0;t+2<tr.Length;t+=3)
            {
                var oldCenter=(oldPoints[tr[t]]+oldPoints[tr[t+1]]+oldPoints[tr[t+2]])/3;
                var newCenter=(newPoints[tr[t]]+newPoints[tr[t+1]]+newPoints[tr[t+2]])/3;
                var d=newCenter-oldCenter;if(d.Length()<.02f)continue;d=NVector.Normalize(d);
                var origin=newCenter+d*.05f;float before=Ray(origin,-d,oldPoints,tr),now=Ray(origin,-d,newPoints,tr);
                if(float.IsFinite(now)&&now+.01f<before)earlierHits++;
            }
            sourceExact&=helper._skinnedMeshRenderer.sharedMesh.vertices.SequenceEqual(originals[id]);
            if(b!=null)cacheExact&=helper._nodeWeights==b.Cache;
            details.Add(new{name=helper.name,vertices=after.Length,affected=mask.Count,moved=count});
        }
        Check("Real front abdomen collision follows expansion",front>15&&maxDistance>.03f&&maxDistance<profile.Span*3);
        float pairRestDistance=NVector.Distance(bodyPairOriginal,collisionPairOriginal);
        float pairBodyForward=bodyPairStatic.Z-bodyPairOriginal.Z,pairCollisionForward=collisionPairStatic.Z-collisionPairOriginal.Z;
        Check("Previously divergent nearby body/collision vertices now receive matching forward deformation",pairRestDistance<.04f&&pairBodyForward>1f&&MathF.Abs(pairBodyForward-pairCollisionForward)<.01f);
        Check("Excluded and unaffected native collision points stay exact",untouched&&finite&&sourceExact&&cacheExact);
        Check("Retired per-pose upper support remains absent",axis.Fold==null&&upper==0);
        Check("Ray samples encounter the expanded direct collision surface earlier",earlierHits>5);
        Check("Per-pose collision work is limited to low-poly vertices",state.Collision.Bindings.Sum(b=>b.Shape.Indices.Length)<500);
        int prevCalls=state.Collision.Bindings.Sum(b=>b.Helper.NativeCalls);foreach(var h in helpers)h.UpdateCollisionMesh();
        Check("Native callbacks cannot resubmit the bound collision meshes",state.Collision.Bindings.Sum(b=>b.Helper.NativeCalls)==prevCalls);
        var oldResults=state.Collision.Bindings.Select(b=>b.Calculated.vertices).ToArray();UpdateBodyCollision(human);
        Check("Repeated identical pose cannot accumulate shape",state.Collision.Bindings.Select((b,i)=>b.Calculated.vertices.SequenceEqual(oldResults[i])).All(x=>x));
        var rig=state.Collision;ReleaseCollision(state);
        Check("Real replay reset restores current native collision geometry",rig.Bindings.All(b=>b.Calculated.vertices.Select((p,i)=>Near(p,new(native[b.HelperId][i].x,native[b.HelperId][i].y,native[b.HelperId][i].z))).All(x=>x)));
        // Rebuild and then native helper mesh/cache recreation exercises identity invalidation.
        UpdateBodyCollision(human);var rebuilt=state.Collision;var target=rebuilt.Bindings[0];CollisionHelperReleasing(target.Helper);
        target.Helper._meshCalc=new Mesh{vertices=target.SourceMesh.vertices,triangles=target.SourceMesh.triangles};target.Helper._meshCollider.sharedMesh=target.Helper._meshCalc;target.Helper._nodeWeights=NativeCache(target.Renderer);CollisionTopologyChanged(target.Helper);
        UpdateBodyCollision(human);Check("Native helper recreation rebuilds against its new cache and mesh",state.Collision.Ready&&state.Collision.Bindings.Any(b=>b.HelperId==target.HelperId&&b.Calculated==target.Helper._meshCalc&&b.Valid));
        var finalRig=state.Collision;FinishCollisionSync();Check("Unload restores all owned native caches",finalRig.Bindings.All(b=>b.Helper._nodeWeights==b.OriginalCache)&&CollisionBindings.Count==0);
        File.WriteAllText(output,JsonSerializer.Serialize(new{sourceVersion="0.2.19",targetVersion="0.2.27",baselineVertices,differenceFromHistoricalStatic=maxStaticError,differenceFromHistoricalBlend=maxBlendError,pairRestDistance,pairBodyForward,pairCollisionForward,lowPolyAffected=rig.Bindings.Sum(b=>b.Mapped),highPolyCollisionPoseVertices=0,moved,front,upper,maxDistance,earlierHits,helpers=details,note="Offline production preparation/cache/geometry model; no live FPS or Obi simulation measurement"},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"DIRECT: lowPoly={rig.Bindings.Sum(b=>b.Mapped)}, moved={moved}, front={front}, support={upper}, baseError={maxStaticError:G4}, blendError={maxBlendError:G4}");
        _state.Clear();_records.Clear();
    }
    static float Ray(NVector origin,NVector direction,NVector[] v,int[] tr)
    {
        float result=float.PositiveInfinity;
        for(int t=0;t+2<tr.Length;t+=3)
        {
            var a=v[tr[t]];var e1=v[tr[t+1]]-a;var e2=v[tr[t+2]]-a;var h=NVector.Cross(direction,e2);float det=NVector.Dot(e1,h);if(MathF.Abs(det)<1e-9f)continue;
            var s=origin-a;float u=NVector.Dot(s,h)/det;if(u<0||u>1)continue;var q=NVector.Cross(s,e1);float w=NVector.Dot(direction,q)/det;if(w<0||u+w>1)continue;
            float hit=NVector.Dot(e2,q)/det;if(hit>=0)result=MathF.Min(result,hit);
        }
        return result;
    }
}


