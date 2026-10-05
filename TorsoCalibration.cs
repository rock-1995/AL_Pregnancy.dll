using Character;
using UnityEngine;

namespace ALPregnancy;

internal static partial class BellyVertexMorph
{
    private sealed class CalibrationPart
    {
        public SkinnedMeshRenderer Renderer;
        public System.Numerics.Vector3[] Vertices;
    }
    private static TorsoProfile CalibrateTorso(Human human,CharaState state,LocalFrame frame)
    {
        var bones=new Dictionary<string,System.Numerics.Vector3>();
        var parts=new List<CalibrationPart>();
        foreach(var smr in human.GameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if(smr==null || smr.sharedMesh==null || !smr.sharedMesh.isReadable || !BodyMeshSelection.IsBodyPiece(smr)) continue;
            string name=smr.sharedMesh.name.ToLowerInvariant();
            if(name.Contains("armleg") || name.Contains("shadow")) continue;
            Matrix4x4 map=Matrix4x4.identity;
            if(smr!=state.SMR && !TryComputeClothBodyMatrix(smr,state.SMR,out map,out _)) continue;
            System.Numerics.Vector3 Local(Vector3 v)
            {
                var d=map.MultiplyPoint3x4(v)-frame.Center;
                return new System.Numerics.Vector3(Vector3.Dot(d,frame.Right),Vector3.Dot(d,frame.Up),Vector3.Dot(d,frame.Fwd));
            }
            Transform[] rig=smr.bones;
            Matrix4x4[] bind=smr.sharedMesh.bindposes;
            for(int i=0;i<Math.Min(rig.Length,bind.Length);i++)
                if(rig[i]!=null && !bones.ContainsKey(rig[i].name))
                {
                    var pose=MatrixBridge.ToManaged(bind[i]);
                    if(!System.Numerics.Matrix4x4.Invert(pose,out var inverse)) continue;
                    var p=inverse.Translation;
                    bones.Add(rig[i].name,Local(new Vector3(p.X,p.Y,p.Z)));
                }
            // Called once before the first deformation in this lease.
            Vector3[] vertices=smr.sharedMesh.vertices;
            parts.Add(new CalibrationPart { Renderer=smr, Vertices=Array.ConvertAll(vertices,Local) });
        }
        if(!bones.TryGetValue("cf_s_kokanskin",out var floor) ||
           !bones.TryGetValue("cf_s_waist01",out var navel) ||
           !bones.TryGetValue("cf_s_spine03",out var chest))
            throw new InvalidOperationException("Growth model needs pelvis, waist and upper-spine rest landmarks.");
        var visiblePoints=new List<System.Numerics.Vector3>();
        foreach(var part in parts)
            if(part.Renderer.enabled && part.Renderer.gameObject.activeInHierarchy)
                visiblePoints.AddRange(part.Vertices);
        var points=visiblePoints.ToArray();
        if(points.Length<50 || points.Min(v=>v.Y)>floor.Y || points.Max(v=>v.Y)<chest.Y)
        {
            // Clothes may hide all skin. Prefer a registered full-body variant for
            // calibration only; it is never activated or assigned to a renderer.
            var fallback=parts.OrderByDescending(p=>p.Renderer.sharedMesh.name.Contains("onepi"))
                .ThenByDescending(p=>p.Vertices.Length).FirstOrDefault();
            points=fallback?.Vertices ?? throw new InvalidOperationException("No complete readable torso for growth calibration.");
        }
        var profile=TorsoProfile.Build(points,floor.Y,navel.Y,chest.Y);
        RuntimeLogInfo($"[VtxMorph] GrowthCalibration samples={points.Length} floor={profile.PelvicFloor:F5} pubis={profile.Pubis:F5} navel={profile.Navel:F5} ribs={profile.Ribs:F5} span={profile.Span:F5}");
        return profile;
    }
}
