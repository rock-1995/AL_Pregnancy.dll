using Character;
using UnityEngine;
using System.Text.Json;

namespace ALPregnancy;

internal static partial class BellyVertexMorph
{
    // Only written when the user presses the diagnostic button. These numeric
    // snapshots allow offline coordinate/shape checks without operating the game.
    private static void WriteGeometrySnapshot(Human human, CharaState state, List<MeshRecord> records)
    {
        try
        {
            BodyMeshSelection.Register(human);
            if(state!=null && records!=null)UpdateVirtualState(state,records);
            var meshes = new List<object>();
            foreach (var smr in human.GameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || smr.sharedMesh == null || !smr.sharedMesh.isReadable) continue;
                bool body = BodyMeshSelection.IsBodyPiece(smr);
                bool cloth = IsBodyMorphClothSMR(smr, state?.SMR);
                if (!body && !cloth) continue;
                bool active = smr.enabled && smr.gameObject.activeInHierarchy;
                Mesh mesh = smr.sharedMesh;
                var record = records == null ? null : FindRecord(records, mesh);
                Transform[] bones = smr.bones;
                Matrix4x4[] poses = mesh.bindposes;
                Vector3[] current = active ? mesh.vertices : null;
                Vector3[] original = active ? record?.OrigVerts ?? current : null;
                BoneWeight[] weights = active ? mesh.boneWeights : null;
                // Capture the actual pose and body-shape bone scales. Rest-space
                // triangles alone cannot reveal folds introduced by skinning a
                // large belly displacement with neighbouring different bones.
                var skinToWorld = active ? new System.Numerics.Matrix4x4[poses.Length] : null;
                var rendererToWorld = MatrixBridge.ToManaged(smr.transform.localToWorldMatrix);
                if (skinToWorld != null)
                    for (int i=0;i<skinToWorld.Length;i++)
                        skinToWorld[i] = i<bones.Length && bones[i]!=null
                            ? MatrixBridge.ToManaged(poses[i])*MatrixBridge.ToManaged(bones[i].localToWorldMatrix)
                            : rendererToWorld;
                meshes.Add(new
                {
                    renderer = smr.name, mesh = mesh.name, body, cloth, active,
                    rootBone=smr.rootBone?.name, updateWhenOffscreen=smr.updateWhenOffscreen,
                    localBounds=new {center=Components(smr.localBounds.center),size=Components(smr.localBounds.size)},
                    worldBounds=new {center=Components(smr.bounds.center),size=Components(smr.bounds.size)},
                    boundsMode=record?.Virtual==null?"native":"bone-envelope-world",
                    originalNormals=active && record?.OrigNormals!=null?Array.ConvertAll(record.OrigNormals,Components):null,
                    normals=active?Array.ConvertAll((Vector3[])mesh.normals,Components):null,
                    tangents=active?Array.ConvertAll((Vector4[])mesh.tangents,t=>new[]{t.x,t.y,t.z,t.w}):null,
                    boneNames = Array.ConvertAll(bones, b => b?.name ?? ""),
                    bindposesRowMajor = Array.ConvertAll(poses, m => MatrixValues(MatrixBridge.ToManaged(m))),
                    originalBindposesRowMajor=record?.Virtual==null?null:Array.ConvertAll(record.Virtual.Binds,m=>MatrixValues(MatrixBridge.ToManaged(m))),
                    virtualBoneIndex=record?.Virtual?.Binds.Length ?? -1,
                    virtualBlend=record?.Virtual?.Blend,
                    upperFoldMoved=record?.Virtual?.Fold?.Moved ?? 0,
                    upperFoldSingular=record?.Virtual?.Fold?.Singular ?? 0,
                    upperFoldMaxWorld=record?.Virtual?.Fold?.MaxWorld ?? 0,
                    virtualPaletteRecipes=record?.Virtual?.Recipes.Select(r=>new {bone=r.Bone,virtualShare=r.VirtualShare}).ToArray(),
                    originalSkinWeights=record?.Virtual==null?null:Array.ConvertAll(record.Virtual.Weights,w=>new float[]{w.boneIndex0,w.weight0,w.boneIndex1,w.weight1,w.boneIndex2,w.weight2,w.boneIndex3,w.weight3}),
                    skinToWorldRowMajor = skinToWorld == null ? null : Array.ConvertAll(skinToWorld, MatrixValues),
                    rendererToWorldRowMajor = MatrixValues(rendererToWorld),
                    toReferenceRowMajor = record == null ? null : MatrixValues(record.ToReference),
                    vertices = original == null ? null : Array.ConvertAll(original, Components),
                    deformed = current == null ? null : Array.ConvertAll(current, Components),
                    staticDeformed = active && record?.LastNewV!=null ? Array.ConvertAll(record.LastNewV,Components) : null,
                    triangles = active ? (int[])mesh.triangles : null,
                    influence = record?.BellyInfluence,
                    breastWeights = record?.BreastWeights,
                    breastExcluded = record?.BreastExcluded,
                    skinWeights = weights == null ? null : Array.ConvertAll(weights, w => new float[] { w.boneIndex0, w.weight0, w.boneIndex1, w.weight1, w.boneIndex2, w.weight2, w.boneIndex3, w.weight3 })
                });
            }
            object frame = state == null ? null : new
            {
                center = Components(state.Frame.Center), up = Components(state.Frame.Up),
                right = Components(state.Frame.Right), forward = Components(state.Frame.Fwd),
                boneLength = state.Frame.BoneLen, rate = state.LastAppliedRate
            };
            string directory = Path.Combine(BepInEx.Paths.PluginPath, "AL_Pregnancy", "diagnostics");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "latest-mesh.json");
            // Named float handling makes pre-preview snapshots (rate=NaN) valid JSON too.
            var options = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
            var rig=state?.Virtual;
            object virtualAxis=rig==null?null:new {pelvis=rig.Pelvis?.name,spine=rig.Spine?.name,anchor=Vec(rig.Reference.Anchor),upper=Vec(rig.Reference.Upper),angle=rig.Last.AngleDegrees,pull=rig.Last.Pull,transform=MatrixValues(rig.Last.Transform),failed=rig.Failed,
                updateCount=rig.UpdateCount,paletteWrites=rig.PaletteWrites,
                meanUpdateMs=rig.UpdateCount==0?0:rig.UpdateTicks*1000.0/System.Diagnostics.Stopwatch.Frequency/rig.UpdateCount,
                maxUpdateMs=rig.MaxTicks*1000.0/System.Diagnostics.Stopwatch.Frequency};
            var support=rig?.Fold;
            object upperFold=support==null?null:new {failed=support.Failed,validColumns=support.Field.ValidColumns,activeColumns=support.Field.ActiveColumns,
                low=support.Field.Low,high=support.Field.High,maxNormalizedCorrection=support.Field.MaxCorrection,writes=support.Writes,
                movedVertices=support.Meshes.Sum(m=>m.Moved),singularVertices=support.Meshes.Sum(m=>m.Singular),hiddenReferences=support.Sources.Count(s=>s.ReadOnlySource)};
            File.WriteAllText(path, JsonSerializer.Serialize(new { version = "0.2.27", deformationMode = "native-completion-material-height-no-boundary-rings", frame, anatomy = state?.Profile, virtualAxis, upperFold,
                collision = CollisionSnapshot(human),
                navelResponse=BellyShape.NavelStageResponse(state?.LastAppliedRate??0,BellyDeformSettings.Vtx), settings = BellyDeformSettings.Vtx, meshes }, options));
            Log.LogInfo("[VtxDump] Geometry snapshot written: " + path);
        }
        catch (Exception ex) { Log.LogWarning("[VtxDump] Geometry snapshot: " + ex); }
    }

    private static float[] Components(Vector3 v) => new[] { v.x, v.y, v.z };
    private static float[] Vec(System.Numerics.Vector3 v)=>new[]{v.X,v.Y,v.Z};
    private static float[] MatrixValues(System.Numerics.Matrix4x4 m) => new[]
    {
        m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44
    };
}



