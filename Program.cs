using ALPregnancy;
using UnityEngine;
using Obi;
using System.Text.Json;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NVector=System.Numerics.Vector3;
using NMatrix=System.Numerics.Matrix4x4;
BellyVertexMorph.RunRuntimeRegression(args);

namespace ALPregnancy
{
    internal static partial class BellyVertexMorph
    {
        static int Checks;
        static void Check(string name,bool value){if(!value)throw new Exception("FAIL: "+name);Checks++;Console.WriteLine("PASS: "+name);}
        static bool Near(Vector3 a,NVector b,float tolerance=2e-5f)=>NVector.Distance(new(a.x,a.y,a.z),b)<tolerance;
        static Matrix4x4 U(NMatrix m)=>new(){Value=m};
        static TorsoProfile Profile()=>new(){PelvicFloor=-.3f,Pubis=0,Navel=.4f,Ribs=1,SampleMin=-.3f,SampleMax=1.2f,Front=new[]{.2f,.2f,.2f},Back=new[]{-.2f,-.2f,-.2f},Width=new[]{.3f,.3f,.3f}};
        static Il2CppReferenceArray<SkinnedCollisionHelper.WeightList> NativeCache(SkinnedMeshRenderer renderer)
        {
            var m=renderer.sharedMesh;var source=m.vertices;var groups=new List<SkinnedCollisionHelper.WeightList>();
            for(int g=0;g<renderer.bones.Length;g++)
            {
                var list=new Il2CppSystem.Collections.Generic.List<SkinnedCollisionHelper.VertexWeight>();
                for(int i=0;i<source.Length;i++)foreach(var w in Weights(m.boneWeights[i]))
                    if(w.Bone==g&&w.Weight>0)list.Add(new(){Index=i,Weight=w.Weight,LocalPosition=ToUnity(NVector.Transform(new(source[i].x,source[i].y,source[i].z),m.bindposes[g].Value)),LocalNormal=new(0,0,1)});
                groups.Add(new(){Transform=renderer.bones[g],Weights=list});
            }
            return new(groups.ToArray());
        }
        static SkinnedCollisionHelper Helper(SkinnedMeshRenderer renderer)
        {
            var mesh=renderer.sharedMesh;var calc=new Mesh{vertices=mesh.vertices,triangles=(int[])mesh.triangles.Clone()};
            var h=new SkinnedCollisionHelper{_skinnedMeshRenderer=renderer,_meshCalc=calc,_meshCollider=new(){sharedMesh=calc},_newVert=mesh.vertices,Before=BeforeNativeCollision,After=AfterNativeCollision};
            h._meshCollider.transform=h.transform;h._nodeWeights=NativeCache(renderer);return h;
        }
        static (Character.Human Human,CharaState State,CollisionBinding Binding,SkinnedCollisionHelper Helper,ObiMeshShapeTracker Tracker) Setup(bool dynamic=true)
        {
            var original=new[]{new NVector(0,0,0),NVector.UnitX,NVector.UnitY,new NVector(2,2,2)};
            var bones=new[]{new Transform{name="waist"},new Transform{name="spine"}};
            var weights=Enumerable.Repeat(new BoneWeight{boneIndex0=0,weight0=.4f,boneIndex1=1,weight1=.6f},4).ToArray();
            var mesh=new Mesh{vertices=original.Select(ToUnity).ToArray(),triangles=new[]{0,1,2},bindposes=new[]{Matrix4x4.identity,Matrix4x4.identity},boneWeights=weights};
            var renderer=new SkinnedMeshRenderer{sharedMesh=mesh,bones=bones};var helper=Helper(renderer);
            var human=new Character.Human{Pointer=new IntPtr(helper.GetInstanceID())};human.GameObject.Components=new object[]{helper};
            var state=new CharaState{HumanPtr=human.Pointer,SMR=renderer,Profile=Profile()};
            if(dynamic)state.Virtual=new(){Pelvis=bones[0],Last=new(NMatrix.CreateTranslation(0,0,.6f),0,0,default,default,default)};
            var rig=new CollisionRig{State=state,ExpectedVirtual=state.Virtual,Revision=CollisionRevision,Ready=true};state.Collision=rig;
            var shape=new CollisionDeformation{Original=original,Static=original.Select((p,i)=>i<2?p+NVector.UnitZ*.4f:p).ToArray(),Material=original,
                Blend=new[]{.5f,.5f,0,0},Candidates=new bool[4],Indices=new[]{0,1},World=new NVector[4],Support=new NVector[4],CarrierLocal=new NVector[4]};
            var b=new CollisionBinding{Rig=rig,Helper=helper,HelperId=helper.GetInstanceID(),CalculatedId=helper._meshCalc.GetInstanceID(),Renderer=renderer,SourceMesh=mesh,Calculated=helper._meshCalc,Collider=helper._meshCollider,
                Shape=shape,ToReference=NMatrix.Identity,Mapped=2,LastWritten=new NVector[4],OriginalCache=helper._nodeWeights,
                Skin=new(){Bones=bones,Binds=mesh.bindposes,Weights=weights,NativeMatrices=new NMatrix[2],NativeTicks=new int[2]}};
            rig.Bindings=new[]{b};BuildCollisionCache(b,dynamic);UpdateCollisionInputs(b);helper._nodeWeights=b.Cache;CollisionBindings[b.HelperId]=b;
            _state[(int)human.Pointer]=state;_records[(int)human.Pointer]=new();
            return(human,state,b,helper,ObiColliderWorld.instance.Register(b.Collider));
        }
        static void UpdateObi(){BeforeCollisionWorldUpdate(ObiColliderWorld.instance);ObiColliderWorld.instance.UpdateWorld();}
        internal static void RunRuntimeRegression(string[] args)
        {
            InstallCollisionSync(new HarmonyLib.Harmony());
            var a=Setup();var src=a.Binding.SourceMesh.vertices;var originalCache=a.Binding.OriginalCache;
            a.Helper.UpdateCollisionMesh();Check("Early helper entry waits for the same actor's completed native update",a.Helper.NativeCalls==0);
            int writes=a.Binding.Calculated.VertexWrites;int assigns=a.Binding.Collider.Assignments;
            UpdateBodyCollision(a.Human);var first=a.Binding.Calculated.vertices;
            Check("Direct low-poly static and virtual rules reach the native output",Near(first[0],new(0,0,.7f))&&Near(first[1],new(1,0,.7f)));
            Check("Unchanged points retain native skinning",first[2].Equals(src[2])&&first[3].Equals(src[3]));
            Check("One completed update has exactly one native mesh write and cook",a.Helper.NativeCalls==1&&a.Helper.Recooks==1&&a.Binding.Calculated.VertexWrites==writes+1&&a.Binding.Collider.Assignments==assigns);
            Check("Source vertices and native topology stay intact",a.Binding.SourceMesh.vertices.SequenceEqual(src)&&a.Binding.Calculated.triangles.SequenceEqual(new[]{0,1,2}));
            Check("Original native cache remains immutable and available for restore",originalCache[0].Weights[0].LocalPosition.Equals(src[0])&&originalCache[0].Weights.Count==4);
            Check("Obi is deferred until its native read boundary",a.Tracker.Invalidations==0&&DirtyCollisionMeshes.ContainsKey(a.Binding.CalculatedId));
            a.Helper.UpdateCollisionMesh();Check("Later helper entry does not repeat the commit",a.Helper.NativeCalls==1&&a.Binding.Calculated.VertexWrites==writes+1);
            UpdateObi();a.Tracker.Read();Check("Obi reads the single final native result",a.Tracker.Invalidations==1&&a.Tracker.Cached.SequenceEqual(first));
            UpdateBodyCollision(a.Human);UpdateObi();
            Check("No time/frame gating: another completed native update runs immediately",a.Helper.NativeCalls==2);
            Check("An unchanged final shape does not invalidate Obi again",a.Tracker.Invalidations==1);
            a.Binding.Skin.Bones[1].localToWorldMatrix=U(NMatrix.CreateTranslation(.5f,.2f,0));UpdateBodyCollision(a.Human);
            Check("Low-poly virtual mixture follows changing native spine pose",Near(a.Binding.Calculated.vertices[0],new(.15f,.06f,.7f)));
            a.Binding.Skin.Bones[0].localToWorldMatrix=U(NMatrix.CreateTranslation(2,3,4));
            a.State.Virtual.Last=new(NMatrix.CreateTranslation(2,3,4.6f),0,0,default,default,default);UpdateBodyCollision(a.Human);
            Check("Carrier motion is evaluated in its current local frame",Near(a.Binding.Calculated.vertices[0],new(1.55f,2.16f,3.5f)));
            a.Tracker.Fail=true;UpdateObi();int errors=Log.Errors;UpdateObi();
            Check("Failed Obi cache refresh remains pending without log spam",DirtyCollisionMeshes.ContainsKey(a.Binding.CalculatedId)&&Log.Errors==errors);
            a.Tracker.Fail=false;UpdateObi();Check("Failed Obi refresh retries at its next native read",!DirtyCollisionMeshes.ContainsKey(a.Binding.CalculatedId));
            ReleaseCollision(a.State);UpdateObi();a.Tracker.Read();
            Check("Reset restores the exact original native cache object",a.Helper._nodeWeights==originalCache&&!CollisionBindings.ContainsKey(a.Binding.HelperId));
            Check("Reset uses current native animation, not the old pose",Near(a.Binding.Calculated.vertices[0],new(1.1f,1.32f,1.6f))&&a.Tracker.Cached.SequenceEqual(a.Binding.Calculated.vertices));
            a.Helper.UpdateCollisionMesh();Check("Native helper is unrestricted after release",a.Helper.NativeCalls>=5);
            var s=Setup(false);UpdateBodyCollision(s.Human);
            Check("Static-only deformation is baked into native input",s.Binding.DynamicWeights==null&&Near(s.Binding.Calculated.vertices[0],new(0,0,.4f)));
            s.Binding.Skin.Bones[1].localToWorldMatrix=U(NMatrix.CreateRotationY(.4f));UpdateBodyCollision(s.Human);
            var expected=NVector.Transform(new(0,0,.4f),NMatrix.Identity*.4f+NMatrix.CreateRotationY(.4f)*.6f);
            Check("Baked static collision follows later bone rotation",Near(s.Binding.Calculated.vertices[0],expected));ReleaseCollision(s.State);
            var pose=Setup();float maxPoseError=0;
            for(int k=0;k<=90;k++)
            {
                float angle=k*MathF.PI/180;
                var pelvis=NMatrix.CreateRotationY(angle*.3f)*NMatrix.CreateTranslation(1,2,3);
                var spine=NMatrix.CreateRotationX(angle)*pelvis;
                var axis=NMatrix.CreateRotationX(angle*.4f)*pelvis;
                pose.Binding.Skin.Bones[0].localToWorldMatrix=U(pelvis);pose.Binding.Skin.Bones[1].localToWorldMatrix=U(spine);
                pose.State.Virtual.Last=new(axis,0,0,default,default,default);
                var helperSpace=NMatrix.CreateRotationZ(.2f)*NMatrix.CreateTranslation(-2,1,4);pose.Helper.transform.localToWorldMatrix=U(helperSpace);
                UpdateBodyCollision(pose.Human);NMatrix.Invert(helperSpace,out var inv);
                for(int i=0;i<2;i++)
                {
                    var target=NVector.Transform(NVector.Lerp(NVector.Transform(pose.Binding.Shape.Static[i],pelvis*.4f+spine*.6f),NVector.Transform(pose.Binding.Shape.Static[i],axis),.5f),inv);
                    var actual=pose.Binding.Calculated.vertices[i];maxPoseError=MathF.Max(maxPoseError,NVector.Distance(target,new(actual.x,actual.y,actual.z)));
                }
            }
            Check("91 bending/twisting poses agree with independent low-poly skinning in transformed helper space",maxPoseError<2e-5f);
            Check("Every completed pose still submits once",pose.Helper.NativeCalls==91&&pose.Helper.Recooks==91);ReleaseCollision(pose.State);
            var bad=Setup();UpdateBodyCollision(bad.Human);bad.Binding.Shape.Static[0]=new(float.NaN,0,0);UpdateBodyCollision(bad.Human);
            Check("Nonfinite pose restores every input cache and disables sync",bad.Binding.Rig.Failed&&!bad.Binding.Rig.Ready&&bad.Helper._nodeWeights==bad.Binding.OriginalCache&&Near(bad.Binding.Calculated.vertices[0],NVector.Zero));
            var replaced=Setup();UpdateBodyCollision(replaced.Human);var replacementCache=NativeCache(replaced.Binding.Renderer);replaced.Helper._nodeWeights=replacementCache;replaced.Helper.UpdateCollisionMesh();
            Check("Native cache replacement cannot be overwritten by stale ownership",replaced.Helper._nodeWeights==replacementCache&&Near(replaced.Binding.Calculated.vertices[0],NVector.Zero));
            var sourceSwap=Setup();UpdateBodyCollision(sourceSwap.Human);sourceSwap.Binding.Renderer.sharedMesh=new Mesh();ReleaseCollision(sourceSwap.State);
            Check("Source replacement cannot leave our deformation in the surviving native output",sourceSwap.Helper._nodeWeights==sourceSwap.Binding.OriginalCache&&Near(sourceSwap.Binding.Calculated.vertices[0],NVector.Zero));
            var actor1=Setup();var actor2=Setup();UpdateBodyCollision(actor1.Human);UpdateBodyCollision(actor2.Human);ReleaseCollision(actor1.State);
            Check("One actor's release cannot restore another actor's cache",actor2.Helper._nodeWeights==actor2.Binding.Cache&&CollisionBindings.ContainsKey(actor2.Binding.HelperId));
            UpdateBodyCollision(actor2.Human);Check("Independent actor continues after neighbour release",Near(actor2.Binding.Calculated.vertices[0],new(0,0,.7f)));ReleaseCollision(actor2.State);
            var released=Setup();UpdateBodyCollision(released.Human);CollisionHelperReleasing(released.Helper);
            Check("Native Release restores inputs before dropping ownership",released.Helper._nodeWeights==released.Binding.OriginalCache&&!CollisionBindings.ContainsKey(released.Binding.HelperId));
            var destroyed=Setup();int deadId=destroyed.Binding.HelperId;destroyed.Binding.Helper=null;ReleaseCollision(destroyed.State);Check("Destroyed helpers leave no stale bindings",!CollisionBindings.ContainsKey(deadId));
            CollisionTopologyChanged(a.Helper);int revision=CollisionRevision;CollisionTopologyChanged(a.Helper);Check("Repeated Init on the same native mesh does not rebuild",CollisionRevision==revision);
            Check("Helper and renderer enable state is retained",a.Helper.enabled&&a.Binding.Renderer.enabled&&a.Binding.Collider.enabled);
            foreach(var state in _state.Values)ReleaseCollision(state);_state.Clear();_records.Clear();
            RunObiIndexRegression();
            if(args.Length>0)Replay(args[0],args.Length>1?args[1]:"collision-direct-replay.json");
            Console.WriteLine($"{Checks} collision runtime/direct geometry checks passed (native cache model, not live Unity/Obi).");
        }
    }
}

