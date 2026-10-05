using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace ALPregnancy;

internal static partial class BellyVertexMorph
{
    private sealed class FoldMesh
    {
        internal MeshRecord Record;
        internal VirtualBinding Binding;
        internal bool ReadOnlySource;
        internal NVector[] Material, Base, Current, World, BaseNormals, Normals, Correction;
        internal NVector4[] BaseTangents, Tangents;
        internal NMatrix[] Skin;
        internal bool[] Candidates, Needed;
        internal int[] Indices;
        internal PoseSurfaceNormals Shading;
        internal Il2CppStructArray<Vector3> Vertices, NormalBuffer;
        internal Il2CppStructArray<Vector4> TangentBuffer;
        internal bool Written;
        internal int Moved, Singular;
        internal float MaxWorld;
        internal void Restore()
        {
            if (!Written) return;
            if (Record.Mesh != null)
            {
                Record.Mesh.vertices = ToUnityVectors(Base);
                Record.Mesh.normals = ToUnityVectors(BaseNormals);
                if (BaseTangents.Length > 0) Record.Mesh.tangents = BaseTangents.Select(x => new Vector4(x.X, x.Y, x.Z, x.W)).ToArray();
            }
            Array.Copy(Base, Current, Base.Length);
            for (int i = 0; i < Base.Length; i++) Vertices[i] = ToUnity(Base[i]);
            Array.Copy(BaseNormals, Normals, Normals.Length);
            for (int i = 0; i < Normals.Length; i++) NormalBuffer[i] = ToUnity(Normals[i]);
            if (Tangents.Length > 0)
                for (int i = 0; i < Tangents.Length; i++) { Tangents[i] = BaseTangents[i]; var x = Tangents[i]; TangentBuffer[i] = new(x.X, x.Y, x.Z, x.W); }
            Written = false; Moved = 0; MaxWorld = 0;
        }
    }
    private sealed class FoldRig
    {
        internal UpperFoldSupport Field;
        internal FoldMesh[] Sources, Meshes, AllMeshes;
        internal NVector[] Posed;
        internal NMatrix Frame;
        internal float Span;
        internal bool Failed;
        internal long Writes;
    }
    private static Vector3 ToUnity(NVector p) => new(p.X, p.Y, p.Z);
    private static NMatrix NativeMatrix(VirtualBinding binding, int index)
    {
        if (binding.NativeTicks[index] != binding.Tick)
        {
            var bone = binding.Bones[index];
            if (bone == null) throw new InvalidOperationException("Source skin bone destroyed.");
            binding.NativeMatrices[index] = MatrixBridge.ToManaged(binding.Binds[index]) * MatrixBridge.ToManaged(bone.localToWorldMatrix);
            binding.NativeTicks[index] = binding.Tick;
        }
        return binding.NativeMatrices[index];
    }
    private static NMatrix VertexSkin(VirtualBinding binding, int i, NMatrix virtualMesh)
    {
        var w = binding.Weights[i]; var native = new NMatrix();
        if (w.weight0 > 0) native += NativeMatrix(binding, w.boneIndex0) * w.weight0;
        if (w.weight1 > 0) native += NativeMatrix(binding, w.boneIndex1) * w.weight1;
        if (w.weight2 > 0) native += NativeMatrix(binding, w.boneIndex2) * w.weight2;
        if (w.weight3 > 0) native += NativeMatrix(binding, w.boneIndex3) * w.weight3;
        return NMatrix.Lerp(native, virtualMesh, binding.Blend[i]);
    }

    private static void PrepareUpperFold(CharaState state, List<MeshRecord> records)
    {
        if (state.Virtual == null || BellyDeformSettings.Vtx.UpperFoldSupport <= 0) return;
        var frame = FrameMatrix(state.Frame);
        if (!NMatrix.Invert(frame, out var inverse)) return;
        float span = state.Profile.Span;
        var bodies = records.Where(r => r.Renderer != null && !r.IsCloth && r.LastNewV != null && r.ActualMoved > 0 && BodyMeshSelection.IsBodyPiece(r.Renderer) &&
            !(r.Mesh.name ?? "").Contains("shadow", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (bodies.Length == 0) return;
        NVector[] Material(MeshRecord r) => r.OrigVerts.Select(x => NVector.Transform(new NVector(x.x, x.y, x.z), r.ToReference * inverse) / span).ToArray();
        var surfaces = bodies.Select(r => new UpperFoldSupport.Surface(Material(r), (int[])r.Mesh.triangles, r.Virtual == null ? 1 : 0)).ToArray();
        var field = new UpperFoldSupport(state.Profile, surfaces);
        if (field.ValidColumns < 3) { Log.LogWarning("[UpperFold] Not enough continuous body surface; support skipped."); return; }
        var meshes = new List<FoldMesh>();
        foreach (var r in records)
        {
            if (r.Virtual == null || r.LastNewV == null) continue;
            var material = Material(r); var candidates = material.Select(field.Contains).ToArray();
            for (int i = 0; i < candidates.Length; i++) if (BreastExclusion.Contains(r.BreastExcluded, i)) candidates[i] = false;
            if (!candidates.Any(x => x) && !bodies.Contains(r)) continue;
            var n = ToManagedVectors((Vector3[])r.Mesh.normals);
            if (n.Length != material.Length) continue;
            var uv = (Vector4[])r.Mesh.tangents;
            var tangent = uv.Length == material.Length ? uv.Select(x => new NVector4(x.x, x.y, x.z, x.w)).ToArray() : Array.Empty<NVector4>();
            var verts = ToManagedVectors(r.LastNewV);
            var groups = r.NormalWeldGroup ?? Enumerable.Range(0, verts.Length).ToArray();
            var mesh = new FoldMesh { Record = r, Binding = r.Virtual, Material = material, Base = verts, Current = (NVector[])verts.Clone(),
                World = new NVector[verts.Length], Correction = new NVector[verts.Length], Skin = new NMatrix[verts.Length], Candidates = candidates, Needed = (bool[])candidates.Clone(),
                BaseNormals = n, Normals = (NVector[])n.Clone(), BaseTangents = tangent, Tangents = (NVector4[])tangent.Clone(),
                Vertices = new Il2CppStructArray<Vector3>(r.LastNewV), NormalBuffer = new Il2CppStructArray<Vector3>((Vector3[])r.Mesh.normals),
                TangentBuffer = tangent.Length == 0 ? null : new Il2CppStructArray<Vector4>(uv),
                Shading = new PoseSurfaceNormals(verts, (int[])r.Mesh.triangles, groups, candidates) };
            r.Virtual.Fold = mesh; meshes.Add(mesh);
        }
        // Garments can hide a whole torso piece. Supplement missing visible
        // triangles with cached hidden-body geometry, evaluated in memory only.
        // Its renderer, weights, vertices and activation state are never changed.
        var sources = new FoldMesh[bodies.Length];
        for (int s = 0; s < bodies.Length; s++)
        {
            var r = bodies[s];
            if (r.Virtual?.Fold != null) { sources[s] = r.Virtual.Fold; continue; }
            if (r.Virtual != null) return;
            var material = surfaces[s].Material; var verts = ToManagedVectors(r.LastNewV);
            var binding = new VirtualBinding { Bones = r.Renderer.bones, Binds = r.Mesh.bindposes, Weights = r.Mesh.boneWeights, Blend = new float[verts.Length] };
            if (binding.Binds.Length != binding.Bones.Length || binding.Weights.Length != verts.Length) return;
            binding.NativeMatrices = new NMatrix[binding.Binds.Length]; binding.NativeTicks = new int[binding.Binds.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var o = r.OrigVerts[i]; float displacement = NVector.TransformNormal(verts[i] - new NVector(o.x, o.y, o.z), r.ToReference).Length();
                binding.Blend[i] = VirtualAxisMath.SurfaceWeight(displacement, material[i].Y * span, state.Profile, BellyDeformSettings.Vtx);
                if (BreastExclusion.Contains(r.BreastExcluded, i)) binding.Blend[i] = 0;
            }
            sources[s] = new FoldMesh { Record = r, Binding = binding, ReadOnlySource = true, Material = material, Base = verts,
                World = new NVector[verts.Length], Skin = new NMatrix[verts.Length], Needed = new bool[verts.Length] };
        }
        foreach (var probe in field.Probes)
            if (probe.Valid) { var s = sources[probe.Surface]; s.Needed[probe.A] = s.Needed[probe.B] = s.Needed[probe.C] = true; }
        var all = meshes.Concat(sources).Distinct().ToArray();
        foreach (var mesh in all) mesh.Indices = Enumerable.Range(0, mesh.Base.Length).Where(i => mesh.Needed[i]).ToArray();
        state.Virtual.Fold = new FoldRig { Field = field, Sources = sources, Meshes = meshes.ToArray(), AllMeshes = all, Posed = new NVector[field.Probes.Length], Frame = frame, Span = span };
        Log.LogInfo($"[UpperFold] prepared sectors={field.ValidColumns}/{UpperFoldSupport.Columns} probes={field.Probes.Count(x => x.Valid)} meshes={meshes.Count} hiddenReferences={sources.Count(s => s.ReadOnlySource)} candidates={meshes.Sum(m => m.Candidates.Count(x => x))}; shared body/cloth material field");
    }

    private static void UpdateUpperFold(VirtualRig rig, NMatrix pelvis)
    {
        var fold = rig.Fold; if (fold == null || fold.Failed) return;
        try
        {
            if (fold.Sources.Any(s => s.Indices.Length > 0 && (s.Record.Renderer == null || (!s.ReadOnlySource && (!s.Record.Renderer.enabled || !s.Record.Renderer.gameObject.activeInHierarchy)) || s.Record.Renderer.sharedMesh != s.Record.Mesh)))
            { foreach (var m in fold.Meshes) m.Restore(); return; }
            var toWorld = fold.Frame * pelvis;
            if (!NMatrix.Invert(toWorld, out var toLocal)) throw new InvalidOperationException("Invalid support frame.");
            foreach (var m in fold.AllMeshes)
            {
                var binding = m.Binding;
                if (binding == null || m.Record.Renderer == null || m.Record.Renderer.sharedMesh != m.Record.Mesh) continue;
                var virtualMesh = m.Record.ToReference * rig.Last.Transform;
                if (m.ReadOnlySource) binding.Tick++;
                foreach (int i in m.Indices)
                {
                    m.Skin[i] = VertexSkin(binding, i, virtualMesh);
                    m.World[i] = NVector.Transform(m.Base[i], m.Skin[i]);
                }
            }
            for (int i = 0; i < fold.Posed.Length; i++)
            {
                var p = fold.Field.Probes[i]; if (!p.Valid) continue;
                var source = fold.Sources[p.Surface];
                var world = source.World[p.A] * p.Bary.X + source.World[p.B] * p.Bary.Y + source.World[p.C] * p.Bary.Z;
                fold.Posed[i] = NVector.Transform(world, toLocal) / fold.Span;
            }
            fold.Field.Update(fold.Posed, BellyDeformSettings.Vtx.UpperFoldSupport);
            foreach (var m in fold.Meshes)
            {
                var binding = m.Record.Virtual;
                if (binding == null || m.Record.Renderer == null || m.Record.Renderer.sharedMesh != m.Record.Mesh) continue;
                bool changed = false; m.Moved = 0; m.Singular = 0; m.MaxWorld = 0;
                Array.Clear(m.Correction, 0, m.Correction.Length);
                foreach (int i in m.Indices)
                {
                    if (!m.Candidates[i]) continue;
                    var current = NVector.Transform(m.World[i], toLocal) / fold.Span;
                    var delta = fold.Field.Correction(m.Material[i], current, m.Record.IsCloth);
                    if (NVector.TransformNormal(delta * fold.Span, toWorld).LengthSquared() > 1e-14f) m.Correction[i] = delta;
                }
                foreach (int i in m.Indices)
                {
                    if (!m.Candidates[i]) continue;
                    var delta = m.Correction[i];
                    var worldDelta = NVector.TransformNormal(delta * fold.Span, toWorld);
                    var target = m.Base[i];
                    if (worldDelta.LengthSquared() > 1e-14f)
                    {
                        if (NMatrix.Invert(m.Skin[i], out var inverseSkin))
                        {
                            var localDelta = NVector.TransformNormal(worldDelta, inverseSkin);
                            if (float.IsFinite(localDelta.LengthSquared()) && NVector.TransformNormal(localDelta, m.Record.ToReference).Length() < fold.Span * 4f)
                            { target += localDelta; m.Moved++; m.MaxWorld = MathF.Max(m.MaxWorld, worldDelta.Length()); }
                            else m.Singular++;
                        }
                        else m.Singular++;
                    }
                    if (NVector.DistanceSquared(target, m.Current[i]) < 1e-16f) continue;
                    m.Current[i] = target; m.Vertices[i] = ToUnity(target); changed = true;
                }
                if (changed)
                {
                    m.Shading.Apply(m.Current, m.BaseNormals, m.BaseTangents, m.Normals, m.Tangents);
                    BreastExclusion.Restore(m.BaseNormals, m.Normals, m.Record.BreastExcluded);
                    BreastExclusion.Restore(m.BaseTangents, m.Tangents, m.Record.BreastExcluded);
                    for (int i = 0; i < m.Normals.Length; i++) m.NormalBuffer[i] = ToUnity(m.Normals[i]);
                    for (int i = 0; i < m.Tangents.Length; i++) { var t = m.Tangents[i]; m.TangentBuffer[i] = new(t.X, t.Y, t.Z, t.W); }
                    m.Record.Mesh.vertices = m.Vertices; m.Record.Mesh.normals = m.NormalBuffer;
                    if (m.TangentBuffer != null) m.Record.Mesh.tangents = m.TangentBuffer;
                    m.Written = true; fold.Writes++;
                }
                // Baseline bounds are recalculated by UpdateVirtualState each pose.
                var bounds = binding.LastWorldBounds; bounds.Expand(m.MaxWorld * 2);
                m.Record.Renderer.bounds = bounds; binding.LastWorldBounds = bounds;
            }
        }
        catch (Exception e)
        {
            fold.Failed = true;
            foreach (var m in fold.Meshes) try { m.Restore(); } catch { }
            Log.LogError("[UpperFold] Support disabled; base virtual skinning retained: " + e);
        }
    }
}
