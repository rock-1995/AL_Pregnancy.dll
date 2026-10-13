using UnityEngine;
using NVector = System.Numerics.Vector3;
using NMatrix = System.Numerics.Matrix4x4;

namespace ALPregnancy;
internal static class BellyDeformSettings { internal static VtxSettings Vtx = new(); }
internal static class BodyMeshSelection { internal static bool IsBodyPiece(SkinnedMeshRenderer r) => r.name == "body"; }
internal sealed class TestLog
{
    internal void LogInfo(string s) { }
    internal void LogWarning(string s) { }
    internal void LogError(string s) { Console.WriteLine(s); }
}
internal static partial class BellyVertexMorph
{
    private sealed class FoldRig { }
    private sealed class FoldMesh { internal void Restore() { } }
    private sealed class MeshRecord
    {
        internal SkinnedMeshRenderer Renderer;
        internal Mesh Mesh;
        internal bool IsCloth;
        internal int ActualMoved, AppliedSig;
        internal VirtualBinding Virtual;
        internal Vector3[] OrigVerts, LastNewV;
        internal bool[] BreastExcluded;
        internal float[] ProtectionKeep;
        internal float[] BellyInfluence;
        internal int[] NormalWeldGroup;
        internal NMatrix ToReference = NMatrix.Identity;
    }
    private struct LocalFrame { internal Vector3 Center, Right, Up, Fwd; }
    private sealed class CharaState
    {
        internal VirtualRig Virtual;
        internal bool VirtualPending;
        internal float LastAppliedRate;
        internal TorsoProfile Profile;
        internal LocalFrame Frame;
        internal SkinnedMeshRenderer SMR;
        internal int PelvisIdx;
        internal IntPtr HumanPtr;
    }
    private static readonly Dictionary<long, CharaState> _state = new();
    private static readonly Dictionary<long, List<MeshRecord>> _records = new();
    private static readonly TestLog Log = new();
    private static NVector[] ToManagedVectors(Vector3[] v) => Array.ConvertAll(v, p => (NVector)p);
    private static Vector3[] ToUnityVectors(NVector[] v) => Array.ConvertAll(v, p => (Vector3)p);
    private static int Sig(Vector3[] v) { var h = new HashCode(); foreach(var p in v)h.Add((NVector)p); return h.ToHashCode(); }
    private static void RecalculateNormalsWelded(Mesh mesh, Vector3[] v, int[] groups) => mesh.RecalculateNormals();
    private static void RestoreExcludedBreastShading(MeshRecord r) { }
    // NativeMatrix is the existing UpperFoldRuntime helper; test through the same
    // bind*pose convention while isolating the unused fold subsystem.
    private static NMatrix NativeMatrix(VirtualBinding binding, int index)
    {
        if(binding.NativeTicks[index] != binding.Tick)
        {
            if(binding.Bones[index]==null)throw new InvalidOperationException("Source skin bone destroyed.");
            binding.NativeMatrices[index]=MatrixBridge.ToManaged(binding.Binds[index])*MatrixBridge.ToManaged(binding.Bones[index].localToWorldMatrix);
            binding.NativeTicks[index]=binding.Tick;
        }
        return binding.NativeMatrices[index];
    }

    internal static void RuntimeTests()
    {
        var data = Program.Garment(12, 20);
        var native = data.Weights.Select(w => new BoneWeight {boneIndex0=w[0].Bone,weight0=w[0].Weight,
            boneIndex1=w[1].Bone,weight1=w[1].Weight}).ToArray();
        var bones = new Transform[data.Controlled.Length];
        for(int i=0;i<bones.Length;i++)bones[i]=new Transform {name=i==0?"cf_s_waist01":$"cf_j_sk_{(i-1)/3:00}_{(i-1)%3:00}"};
        for(int i=0;i<bones.Length;i++)bones[i].parent=data.Parents[i]<0?null:bones[data.Parents[i]];
        var binds=Array.ConvertAll(data.Roots,p=>MatrixBridge.ToUnity(NMatrix.CreateTranslation(-p)));
        for(int i=0;i<bones.Length;i++)bones[i].localToWorldMatrix=binds[i].inverse;
        var mesh=new Mesh {name="skirt",vertices=ToUnityVectors(data.Original),boneWeights=native,bindposes=binds,triangles=data.Triangles};
        mesh.normals=new Vector3[data.Original.Length];mesh.tangents=new Vector4[data.Original.Length];
        var renderer=new SkinnedMeshRenderer {sharedMesh=mesh,bones=bones};
        var record=new MeshRecord {Renderer=renderer,Mesh=mesh,IsCloth=true,ActualMoved=10,
            OrigVerts=ToUnityVectors(data.Original),LastNewV=ToUnityVectors(data.Deformed),BreastExcluded=data.Excluded};
        var bodyBones=new[]{new Transform {name="cf_s_waist01"},new Transform {name="cf_s_spine01"}};
        var body=new SkinnedMeshRenderer {sharedMesh=new Mesh {name="body",bindposes=new[]{Matrix4x4.identity,Matrix4x4.identity}},bones=bodyBones};
        var state=new CharaState {Profile=Program.Torso,SMR=body,Frame=new LocalFrame {Right=Vector3.right,Up=Vector3.up,Fwd=Vector3.forward}};
        var records=new List<MeshRecord>{record};
        foreach(float strength in new[]{1f,0f})
        {
            BellyDeformSettings.Vtx.VirtualAxisStrength=strength;
            mesh.vertices=record.LastNewV;
            var human=new Character.Human {Pointer=(IntPtr)1234};
            state.HumanPtr=human.Pointer;state.LastAppliedRate=1;state.VirtualPending=true;
            _state[1234]=state;_records[1234]=records;
            Program.Check(record.Virtual==null,"static geometry does not install binding before body customization");
            // Simulate native customization between static geometry and the
            // final callback. The first palette must use this new bone pose.
            var customized=NMatrix.CreateTranslation(2,1,-3);
            foreach(var bone in bodyBones)bone.localToWorldMatrix=MatrixBridge.ToUnity(customized);
            UpdateVirtual(human);
            Program.Check(!state.VirtualPending&&record.Virtual!=null,"same callback consumes pending binding once");
            Program.Near(state.Virtual.Last.Transform.Translation,customized.Translation,"first palette uses customized skeleton");
            var firstBinding=record.Virtual;
            UpdateVirtual(human);
            Program.Check(ReferenceEquals(firstBinding,record.Virtual),"stable pose update retains installed binding");
            Program.Check(record.Virtual?.Skirt!=null,"production runtime installs skirt with axis strength "+strength);
            var binding=record.Virtual;var plan=binding.Skirt.Plan;
            int palette=renderer.bones.Length,writes=mesh.VertexWrites;
            for(int pose=0;pose<4;pose++)
            {
                var world=NMatrix.CreateRotationY(.4f)*NMatrix.CreateTranslation(2,3,-1);
                foreach(var bone in bodyBones)bone.localToWorldMatrix=MatrixBridge.ToUnity(world);
                for(int b=0;b<bones.Length;b++)
                {
                    var swing=pose==0?NMatrix.Identity:NMatrix.CreateRotationX(pose*.17f+b*.015f);
                    bones[b].localToWorldMatrix=MatrixBridge.ToUnity(MatrixBridge.ToManaged(binds[b].inverse)*(data.Controlled[b]?swing:NMatrix.Identity)*world);
                }
                var before=bones.Select(b=>b.localToWorldMatrix.Rows).ToArray();
                UpdateVirtualState(state,records);
                Program.Check(mesh.VertexWrites==writes,"pose uses palette only");
                Program.Check(before.SequenceEqual(bones.Select(b=>b.localToWorldMatrix.Rows)),"native skirt transforms untouched");
                for(int i=0;i<mesh.vertices.Length;i++)
                {
                    var actual=Skin(renderer,i);
                    Program.Check(binding.BoundsEnvelope!=null && InBounds(renderer.bounds,actual),"actual skinned point enclosed");
                    if(pose==0)Program.Near(actual,NVector.Transform(plan.Visual[i],world),"rest visual and residual/palette agree",3e-5f);
                    float free=data.Weights[i].Select((w,k)=>w.Weight*plan.Free[i][k]).Sum();
                    if(free>.99999f)
                    {
                        var expected=NVector.Zero;
                        foreach(var w in data.Weights[i])if(w.Weight>0)
                            expected+=(NVector.Transform(data.Original[i],MatrixBridge.ToManaged(binds[w.Bone])*bones[w.Bone].localToWorldMatrix.Rows)+
                                NVector.TransformNormal(plan.Offsets[w.Bone],state.Virtual.Last.Transform))*w.Weight;
                        Program.Near(actual,expected,"free skirt keeps native sway plus torso carry",3e-5f);
                    }
                }
            }
            ReleaseVirtual(records);
            Program.Check(renderer.bones.SequenceEqual(bones)&&mesh.bindposes.SequenceEqual(binds)&&mesh.boneWeights.SequenceEqual(native),"release restores native binding");
            Program.Check(mesh.vertices.SequenceEqual(record.LastNewV)&&record.AppliedSig==Sig(record.LastNewV),"release restores direct vertices and signature");
            PrepareVirtual(state,records,1);
            Program.Check(renderer.bones.Length==palette,"rebuild does not grow palette");
            if(strength>0)
            {
                var beforeBlend=(float[])record.Virtual.Blend.Clone();
                float start=BellyDeformSettings.Vtx.LowerTransitionStart;
                BellyDeformSettings.Vtx.LowerTransitionStart=.75f;
                ReleaseVirtual(records);state.Virtual=null;state.VirtualPending=true;
                UpdateVirtual(human);
                Program.Check(beforeBlend.Where((a,i)=>Math.Abs(a-record.Virtual.Blend[i])>1e-5f).Any(),"Lower start change reaches the rebuilt production binding");
                Program.Check(state.Virtual.LowerStart==.75f,"diagnostic snapshot reports the installed Lower start");
                ReleaseVirtual(records);state.Virtual=null;BellyDeformSettings.Vtx.LowerTransitionStart=start;
                PrepareVirtual(state,records,1);
            }
            var replacement=new Mesh {name="replacement"};renderer.sharedMesh=replacement;
            UpdateVirtualState(state,records);
            Program.Check(record.Virtual==null&&renderer.sharedMesh==replacement&&mesh.vertices.SequenceEqual(record.LastNewV),"replacement restores old skirt without touching new mesh");
            renderer.sharedMesh=mesh;renderer.bones=bones;
            PrepareVirtual(state,records,1);
            // Simulate missing native physics bone. Production failure path must
            // restore all binding and residual writes, not leave a collapsed hem.
            record.Virtual.Bones[1]=null;
            UpdateVirtualState(state,records);
            Program.Check(record.Virtual==null&&mesh.vertices.SequenceEqual(record.LastNewV),"failure restores direct geometry");
            bones[1]=new Transform {name="cf_j_sk_00_00",localToWorldMatrix=binds[1].inverse};
            PrepareVirtual(state,records,0);
            Program.Check(state.Virtual==null&&record.Virtual==null,"zero stage leaves no binding");
            _state.Clear();_records.Clear();
        }
        BellyDeformSettings.Vtx.VirtualAxisStrength=1;
        AxisPhysicsPaletteTest();
    }

    private static void AxisPhysicsPaletteTest()
    {
        var parent=new Transform{name="cf_j_waist01"};
        var root=new Transform{name="cf_y_waist01",parent=parent};
        var skin=new Transform{name="cf_s_waist01",parent=root};
        var spine=new Transform{name="cf_s_spine01"};
        var positions=new[]{new Vector3(0,0,.6f),new Vector3(.1f,0,.6f),new Vector3(0,.1f,.6f)};
        var weights=Enumerable.Repeat(new BoneWeight{boneIndex0=0,weight0=.7f,boneIndex1=1,weight1=.3f},3).ToArray();
        var binds=new[]{Matrix4x4.identity,Matrix4x4.identity};
        var mesh=new Mesh{name="body",vertices=positions,boneWeights=weights,bindposes=binds,triangles=new[]{0,1,2}};
        var renderer=new SkinnedMeshRenderer{sharedMesh=mesh,bones=new[]{skin,spine}};
        var record=new MeshRecord{Renderer=renderer,Mesh=mesh,ActualMoved=3,OrigVerts=positions,
            LastNewV=positions.Select(p=>p+new Vector3(0,0,1)).ToArray(),ProtectionKeep=new[]{0f,.5f,1f}};
        record.LastNewV[0]=positions[0];
        mesh.vertices=record.LastNewV;
        var setup=new MagicaCloth2.RenderSetupSerializeData{transformArray=new[]{root},
            transformLocalPositions=new[]{new MagicaCloth2.Float3()},
            transformLocalRotations=new[]{new MagicaCloth2.Rotation{value=new(){w=1}}}};
        var human=new Character.Human{Pointer=new IntPtr(731),Body=new(){magicaSpringSetting=new(){Waist=new[]{
            new Character.HumanMagicaSpringSetting.BoneData{Spring=new(){SerializeData=new(){rootBones=new(){root}},
                Data2=new(){initData=new(){clothSetupDataList=new(){setup}}}}}
        }}}};
        var state=new CharaState{Profile=Program.Torso,SMR=renderer,HumanPtr=human.Pointer,LastAppliedRate=1,VirtualPending=true,
            Frame=new LocalFrame{Right=Vector3.right,Up=Vector3.up,Fwd=Vector3.forward}};
        var records=new List<MeshRecord>{record};_state[731]=state;_records[731]=records;
        UpdateVirtual(human);
        Program.Check(record.Virtual!=null&&record.Virtual.Blend.Any(a=>a>0),"physical driver integration creates real virtual weights");
        Program.Check(record.Virtual.Blend[0]==0,"fully guarded vertex never enters virtual palette");
        float unguarded=VirtualAxisMath.MaterialSurfaceWeight(1,(NVector)positions[1],Program.Torso,1,BellyDeformSettings.Vtx);
        Program.Check(MathF.Abs(record.Virtual.Blend[1]-unguarded*.5f)<1e-6f,"partial guard attenuates actual installed virtual weights");
        var expectedAxis=state.Virtual.Last.Transform;
        foreach(float angle in new[]{-.2f,.2f})
        {
            var motion=NMatrix.CreateRotationX(angle)*NMatrix.CreateTranslation(0,0,angle*.1f);
            root.localToWorldMatrix=skin.localToWorldMatrix=MatrixBridge.ToUnity(motion);
            UpdateVirtual(human);
            Program.Near(NVector.Transform(NVector.UnitY,state.Virtual.Last.Transform),NVector.Transform(NVector.UnitY,expectedAxis),"waist jiggle no longer drives abdominal axis");
            for(int i=0;i<positions.Length;i++)
            {
                var point=(NVector)record.LastNewV[i];float alpha=record.Virtual.Blend[i];
                var native=NVector.Transform(point,motion)*.7f+point*.3f;
                var expected=NVector.Lerp(native,NVector.Transform(point,expectedAxis),alpha);
                Program.Near(Skin(renderer,i),expected,"live physical carrier cancels in palette; only native residual keeps physical motion");
            }
        }
        ReleaseVirtual(records);
        Program.Check(mesh.boneWeights.SequenceEqual(weights)&&mesh.bindposes.SequenceEqual(binds),"physics-aware virtual binding restores original skin weights");
        _state.Clear();_records.Clear();
    }
    private static bool InBounds(Bounds box,NVector p)
    {
        var d=NVector.Abs(p-(NVector)box.center);var e=(NVector)box.size*.5f+new NVector(1e-5f);
        return d.X<=e.X&&d.Y<=e.Y&&d.Z<=e.Z;
    }
    private static NVector Skin(SkinnedMeshRenderer r,int vertex)
    {
        var result=NVector.Zero;var p=(NVector)r.sharedMesh.vertices[vertex];
        foreach(var w in SkirtInfluences(r.sharedMesh.boneWeights[vertex]))if(w.Weight>0)
            result+=NVector.Transform(p,MatrixBridge.ToManaged(r.sharedMesh.bindposes[w.Bone])*r.bones[w.Bone].localToWorldMatrix.Rows)*w.Weight;
        return result;
    }
}
