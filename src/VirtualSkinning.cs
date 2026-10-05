using Character;
using UnityEngine;
using NMatrix=System.Numerics.Matrix4x4;
using NVector=System.Numerics.Vector3;
using UMatrix=UnityEngine.Matrix4x4;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace ALPregnancy;

internal static partial class BellyVertexMorph
{
    private sealed class VirtualRig
    {
        public VirtualAxisMath.Axis Reference;
        public Transform Pelvis,Spine;
        public NMatrix PelvisBind,SpineBind;
        public VirtualAxisMath.Result Last;
        public bool Failed;
        public FoldRig Fold;
        public long UpdateTicks,MaxTicks,UpdateCount,PaletteWrites;
    }
    private sealed class VirtualBinding
    {
        public Mesh Mesh;
        public SkinnedMeshRenderer Renderer;
        public Transform[] Bones;
        public BoneWeight[] Weights;
        public UMatrix[] Binds;
        public Il2CppStructArray<UMatrix> Palette;
        public float[] Blend;
        public VirtualWeights.Recipe[] Recipes;
        public NMatrix[] LastMatrices,NativeMatrices;
        public int[] NativeTicks;
        public int Tick;
        public Bounds Bounds;
        public SkinBounds BoundsEnvelope;
        public Bounds LastWorldBounds;
        public bool UpdateWhenOffscreen;
        public SkinQuality Quality;
        public bool HasMatrix;
        public FoldMesh Fold;
        public void Restore()
        {
            try
            {
                Fold?.Restore();
                if(Mesh!=null){Mesh.boneWeights=Weights;Mesh.bindposes=Binds;}
                if(Renderer!=null && Renderer.sharedMesh==Mesh)
                {Renderer.bones=Bones;Renderer.ResetBounds();Renderer.localBounds=Bounds;Renderer.quality=Quality;Renderer.updateWhenOffscreen=UpdateWhenOffscreen;}
            }
            catch(Exception e){Log.LogWarning("[VirtualAxis] Restore: "+e.Message);}
        }
    }
    private static NMatrix FrameMatrix(LocalFrame f)=>new(
        f.Right.x,f.Right.y,f.Right.z,0,f.Up.x,f.Up.y,f.Up.z,0,
        f.Fwd.x,f.Fwd.y,f.Fwd.z,0,f.Center.x,f.Center.y,f.Center.z,1);

    private static void ReleaseVirtual(List<MeshRecord> records)
    {
        if(records==null)return;
        foreach(var record in records)
        {record.Virtual?.Restore();record.Virtual=null;}
    }

    private static void PrepareVirtual(CharaState state,List<MeshRecord> records,float stage)
    {
        state.Virtual=null;
        var settings=BellyDeformSettings.Vtx;
        if(settings.VirtualAxisStrength<=0 || stage<=0 || state.Profile==null)return;
        var reference=VirtualAxisMath.Reference(state.Profile,stage,settings);
        var frame=FrameMatrix(state.Frame);
        reference=new VirtualAxisMath.Axis(NVector.Transform(reference.Anchor,frame),NVector.Transform(reference.Upper,frame));
        Transform[] bones=state.SMR.bones;UMatrix[] binds=state.SMR.sharedMesh.bindposes;
        int spine=-1;float distance=float.MaxValue;
        for(int i=0;i<bones.Length && i<binds.Length;i++)
        {
            if(bones[i]==null || !RigBoneNames.Canonical(bones[i].name).StartsWith("cf_s_spine",StringComparison.Ordinal))continue;
            if(!NMatrix.Invert(MatrixBridge.ToManaged(binds[i]),out var rest))continue;
            float d=NVector.DistanceSquared(reference.Upper,rest.Translation);
            if(d<distance){distance=d;spine=i;}
        }
        if(spine<0 || state.PelvisIdx<0 || state.PelvisIdx>=binds.Length)return;
        state.Virtual=new VirtualRig {Reference=reference,Pelvis=bones[state.PelvisIdx],Spine=bones[spine],
            PelvisBind=MatrixBridge.ToManaged(binds[state.PelvisIdx]),SpineBind=MatrixBridge.ToManaged(binds[spine])};

        // Material correspondences are built only on shape changes. Clothing
        // samples the same material attachment field as its nearest skin patch.
        float span=state.Profile.Span;
        NMatrix.Invert(frame,out var inverseFrame);
        var body=new MaterialBlendField(span);
        var alphaByRecord=new Dictionary<MeshRecord,float[]>();
        foreach(var r in records)
        {
            if(r.Renderer==null || !r.Renderer.enabled || !r.Renderer.gameObject.activeInHierarchy || r.LastNewV==null || r.ActualMoved==0)continue;
            var alpha=new float[r.OrigVerts.Length];alphaByRecord[r]=alpha;
            for(int i=0;i<alpha.Length;i++)
            {
                var o=NVector.Transform(new NVector(r.OrigVerts[i].x,r.OrigVerts[i].y,r.OrigVerts[i].z),r.ToReference);
                var n=NVector.Transform(new NVector(r.LastNewV[i].x,r.LastNewV[i].y,r.LastNewV[i].z),r.ToReference);
                var material=NVector.Transform(o,inverseFrame);
                alpha[i]=VirtualAxisMath.MaterialSurfaceWeight(NVector.Distance(o,n),material,state.Profile,stage,settings,
                    BellyShape.DeformationInfluence(r.BellyInfluence == null ? 1 : r.BellyInfluence[i], material.Y, state.Profile, settings));
                if(BreastExclusion.Contains(r.BreastExcluded,i))alpha[i]=0;
            }
            if(!r.IsCloth)
            {
                if(BodyMeshSelection.IsBodyPiece(r.Renderer))
                    for(int i=0;i<alpha.Length;i++)
                        body.Add(NVector.Transform(new NVector(r.OrigVerts[i].x,r.OrigVerts[i].y,r.OrigVerts[i].z),r.ToReference),alpha[i]);
            }
        }
        foreach(var pair in alphaByRecord)
        {
            var r=pair.Key;var alpha=pair.Value;
            if(r.IsCloth)
                for(int i=0;i<alpha.Length;i++)
                {
                    if(alpha[i]<=0)continue;
                    var o=NVector.Transform(new NVector(r.OrigVerts[i].x,r.OrigVerts[i].y,r.OrigVerts[i].z),r.ToReference);
                    alpha[i]=body.Sample(o,alpha[i]);
                }
            for(int i=0;i<alpha.Length;i++)if(BreastExclusion.Contains(r.BreastExcluded,i))alpha[i]=0;
            if(!alpha.Any(x=>x>1e-6f))continue;
            var mesh=r.Mesh;var renderer=r.Renderer;
            var binding=new VirtualBinding {Mesh=mesh,Renderer=renderer,Bones=renderer.bones,Weights=mesh.boneWeights,
                Binds=mesh.bindposes,Blend=alpha,Bounds=renderer.localBounds,Quality=renderer.quality,UpdateWhenOffscreen=renderer.updateWhenOffscreen};
            if(binding.Weights.Length!=alpha.Length || binding.Binds.Length!=binding.Bones.Length)continue;
            int index=binding.Bones.Length;
            var recipes=new List<VirtualWeights.Recipe>();var recipeIndices=new Dictionary<VirtualWeights.Recipe,int>();
            int Slot(VirtualWeights.Recipe recipe)
            {if(!recipeIndices.TryGetValue(recipe,out int slot)){slot=index+recipes.Count;recipes.Add(recipe);recipeIndices[recipe]=slot;}return slot;}
            var newWeights=(BoneWeight[])binding.Weights.Clone();
            var nativeWeights=new VirtualWeights.Influence[alpha.Length][];
            for(int i=0;i<alpha.Length;i++)
            {
                var bw=binding.Weights[i];
                var inputs=new[]{new VirtualWeights.Influence(bw.boneIndex0,bw.weight0),new VirtualWeights.Influence(bw.boneIndex1,bw.weight1),
                    new VirtualWeights.Influence(bw.boneIndex2,bw.weight2),new VirtualWeights.Influence(bw.boneIndex3,bw.weight3)};
                nativeWeights[i]=inputs;
                var output=VirtualWeights.Blend(inputs,alpha[i],Slot);
                var packed=new BoneWeight();
                for(int k=0;k<output.Length;k++)
                    switch(k){case 0:packed.boneIndex0=output[k].Bone;packed.weight0=output[k].Weight;break;
                        case 1:packed.boneIndex1=output[k].Bone;packed.weight1=output[k].Weight;break;
                        case 2:packed.boneIndex2=output[k].Bone;packed.weight2=output[k].Weight;break;
                        case 3:packed.boneIndex3=output[k].Bone;packed.weight3=output[k].Weight;break;}
                newWeights[i]=packed;
            }
            binding.BoundsEnvelope=SkinBounds.Build(ToManagedVectors(r.LastNewV),nativeWeights,alpha,index);
            binding.Recipes=recipes.ToArray();binding.LastMatrices=new NMatrix[recipes.Count];
            binding.NativeMatrices=new NMatrix[index];binding.NativeTicks=new int[index];
            var newBones=binding.Bones.Concat(Enumerable.Repeat(state.Virtual.Pelvis,recipes.Count)).ToArray();
            var newBinds=binding.Binds.Concat(Enumerable.Repeat(UMatrix.identity,recipes.Count)).ToArray();
            // Register restoration BEFORE the first mutation, including partial failure.
            r.Virtual=binding;binding.Palette=new Il2CppStructArray<UMatrix>(newBinds);
            renderer.bones=newBones;mesh.bindposes=binding.Palette;mesh.boneWeights=newWeights;
            renderer.quality=SkinQuality.Bone4;
            // Keep the skin update alive while recovering from an old culled frame.
            renderer.updateWhenOffscreen=true;
            Log.LogInfo($"[VirtualAxis] mesh={mesh.name} virtualVertices={alpha.Count(x=>x>0)} paletteStart={index} virtualSlots={recipes.Count}; exact four-weight factorization");
        }
        if(!records.Any(r=>r.Virtual!=null)){state.Virtual=null;return;}
        Log.LogInfo($"[VirtualAxis] stage={stage:F3} pelvis={state.Virtual.Pelvis.name} spine={state.Virtual.Spine.name} anchor={reference.Anchor} upper={reference.Upper}; no hierarchy bones added");
        // Rest-height attachment replaces the per-pose geometric support pass.
        UpdateVirtualState(state,records);
    }

    // Called after the game's Human.LateUpdate, plus once immediately on rebuild.
    // Only the matrix palette and bounds change each pose. Material attachment
    // is cached on rebuild; no geometric support or per-vertex upload follows.
    public static void UpdateVirtual(Human human)
    {
        if(human==null)return;
        foreach(var pair in _state)
            if(pair.Value.HumanPtr==human.Pointer && _records.TryGetValue(pair.Key,out var records))
                UpdateVirtualState(pair.Value,records);
    }
    private static void UpdateVirtualState(CharaState state,List<MeshRecord> records)
    {
        var rig=state.Virtual;if(rig==null || rig.Failed)return;
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            if(rig.Pelvis==null || rig.Spine==null)throw new InvalidOperationException("Axis bone destroyed.");
            var carrier=MatrixBridge.ToManaged(rig.Pelvis.localToWorldMatrix);
            if(!NMatrix.Invert(carrier,out var inverseCarrier))throw new InvalidOperationException("Noninvertible virtual carrier.");
            var pelvis=rig.PelvisBind*carrier;
            var spine=rig.SpineBind*MatrixBridge.ToManaged(rig.Spine.localToWorldMatrix);
            rig.Last=VirtualAxisMath.Evaluate(rig.Reference,pelvis,spine,BellyDeformSettings.Vtx);
            foreach(var r in records)
            {
                var binding=r.Virtual;
                if(binding==null || binding.Renderer==null || binding.Renderer.sharedMesh!=binding.Mesh)continue;
                var virtualMesh=r.ToReference*rig.Last.Transform;bool changed=false;binding.Tick++;
                NMatrix Native(int index)
                {
                    return NativeMatrix(binding,index);
                }
                for(int i=0;i<binding.Recipes.Length;i++)
                {
                    var recipe=binding.Recipes[i];var transform=virtualMesh;
                    if(recipe.Bone>=0)transform=NMatrix.Lerp(Native(recipe.Bone),virtualMesh,recipe.VirtualShare);
                    var matrix=transform*inverseCarrier;
                    if(!VirtualAxisMath.Finite(matrix))throw new InvalidOperationException("Nonfinite palette matrix.");
                    if(binding.HasMatrix && matrix==binding.LastMatrices[i])continue;
                    binding.Palette[binding.Binds.Length+i]=MatrixBridge.ToUnity(matrix);
                    binding.LastMatrices[i]=matrix;changed=true;
                }
                if(changed){binding.Mesh.bindposes=binding.Palette;rig.PaletteWrites++;}
                binding.HasMatrix=true;
                // World-space override avoids conflating imported mesh coordinates,
                // renderer coordinates and the root-bone bounds frame.
                var box=binding.BoundsEnvelope.Evaluate(Native,virtualMesh);
                if(!box.Valid)throw new InvalidOperationException("Empty virtual skin bounds.");
                var center=(box.Min+box.Max)*.5f;
                var size=box.Max-box.Min;
                var world=new Bounds(new Vector3(center.X,center.Y,center.Z),new Vector3(size.X,size.Y,size.Z));
                world.Expand(MathF.Max(.001f,size.Length()*.01f));
                binding.Renderer.bounds=world;binding.LastWorldBounds=world;
            }
            // No per-vertex support/normal uploads: the palette drives both bands.
        }
        catch(Exception e)
        {
            rig.Failed=true;ReleaseVirtual(records);
            Log.LogError("[VirtualAxis] Disabled and restored native skinning: "+e);
        }
        finally
        {
            long ticks=System.Diagnostics.Stopwatch.GetTimestamp()-started;
            rig.UpdateTicks+=ticks;rig.MaxTicks=Math.Max(rig.MaxTicks,ticks);rig.UpdateCount++;
        }
    }
}
