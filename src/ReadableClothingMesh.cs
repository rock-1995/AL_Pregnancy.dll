using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;

namespace ALPregnancy;

// A few native AL additional garments (including the heart waist chain) have
// Read/Write disabled. Reconstruct a private readable mesh from the ORIGINAL
// mesh's GPU buffers, never the renderer's posed/skinned output. Runs only when
// acquiring a lease; restores the untouched shared source on release.
internal static class ReadableClothingMesh
{
    internal static Mesh TryCreate(Mesh source, out string reason)
    {
        reason = null;
        Mesh copy = null;
        try
        {
            if (source.blendShapeCount != 0)
                throw new InvalidOperationException("Unreadable blend shapes cannot be preserved by buffer copying.");
            if (source.vertexCount <= 0 || source.vertexBufferCount <= 0)
                throw new InvalidOperationException("No vertex buffers.");
            var attributes = source.GetVertexAttributes();
            if (attributes == null || attributes.Length == 0)
                throw new InvalidOperationException("No vertex layout.");
            var binds = source.bindposes;
            if (binds == null || binds.Length == 0)
                throw new InvalidOperationException("No skin bind poses.");

            copy = new Mesh { name = source.name, hideFlags = HideFlags.DontSave };
            copy.SetVertexBufferParams(source.vertexCount, attributes);
            if (copy.vertexBufferCount != source.vertexBufferCount)
                throw new InvalidOperationException("Vertex stream count changed.");
            for (int stream = 0; stream < source.vertexBufferCount; stream++)
            {
                int stride = source.GetVertexBufferStride(stream);
                if (stride <= 0 || copy.GetVertexBufferStride(stream) != stride)
                    throw new InvalidOperationException("Vertex stride changed.");
                var buffer = source.GetVertexBuffer(stream);
                try
                {
                    var data = Read(buffer, checked(source.vertexCount * stride));
                    copy.InternalSetVertexBufferDataFromArray(stream, data.Cast<Il2CppSystem.Array>(),
                        0, 0, data.Length, 1, MeshUpdateFlags.Default);
                }
                finally { buffer?.Dispose(); }
            }
            int indexSize = source.indexFormat == IndexFormat.UInt32 ? 4 : 2;
            var indexBuffer = source.GetIndexBuffer();
            try
            {
                if (indexBuffer == null) throw new InvalidOperationException("No index buffer.");
                int bytes = checked(indexBuffer.count * indexBuffer.stride);
                if (bytes <= 0 || bytes % indexSize != 0)
                    throw new InvalidOperationException("Invalid index buffer length.");
                var data = Read(indexBuffer, bytes);
                copy.SetIndexBufferParams(bytes / indexSize, source.indexFormat);
                copy.InternalSetIndexBufferDataFromArray(data.Cast<Il2CppSystem.Array>(),
                    0, 0, data.Length, 1, MeshUpdateFlags.Default);
            }
            finally { indexBuffer?.Dispose(); }
            copy.bindposes = binds;
            copy.subMeshCount = source.subMeshCount;
            for (int sub = 0; sub < source.subMeshCount; sub++)
                copy.SetSubMesh(sub, source.GetSubMesh(sub), MeshUpdateFlags.DontRecalculateBounds);
            copy.bounds = source.bounds;
            // Do not install a partial/unskinned copy if a backend rejects a read.
            var vertices = copy.vertices;
            var weights = copy.boneWeights;
            if (!copy.isReadable || vertices.Length != source.vertexCount || weights.Length != source.vertexCount)
                throw new InvalidOperationException("Readable vertex/skin data is incomplete.");
            bool nonzero = false;
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i]; var w = weights[i];
                if (!float.IsFinite(v.x) || !float.IsFinite(v.y) || !float.IsFinite(v.z))
                    throw new InvalidOperationException("Non-finite vertex data.");
                nonzero |= v.x != 0 || v.y != 0 || v.z != 0;
                float sum = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                if (!float.IsFinite(sum) || MathF.Abs(sum - 1f) > .02f ||
                    !ValidWeight(w.weight0, w.boneIndex0, binds.Length) ||
                    !ValidWeight(w.weight1, w.boneIndex1, binds.Length) ||
                    !ValidWeight(w.weight2, w.boneIndex2, binds.Length) ||
                    !ValidWeight(w.weight3, w.boneIndex3, binds.Length))
                    throw new InvalidOperationException("Invalid skin weights from GPU buffer.");
            }
            if (!nonzero) throw new InvalidOperationException("Empty GPU readback.");
            return copy;
        }
        catch (Exception ex)
        {
            if (copy != null) UnityEngine.Object.Destroy(copy);
            reason = ex.Message;
            return null;
        }
    }

    private static bool ValidWeight(float weight, int bone, int count) =>
        float.IsFinite(weight) && weight >= 0 && weight <= 1.001f &&
        (weight == 0 || (bone >= 0 && bone < count));

    private static Il2CppStructArray<byte> Read(GraphicsBuffer buffer, int bytes)
    {
        if (buffer == null || bytes <= 0 || bytes > checked(buffer.count * buffer.stride))
            throw new InvalidOperationException("GPU buffer is shorter than the mesh layout.");
        var data = new Il2CppStructArray<byte>(bytes);
        // Non-generic native entry points avoid stripped IL2CPP generic methods.
        buffer.InternalGetData(data.Cast<Il2CppSystem.Array>(), 0, 0, bytes, 1);
        return data;
    }
}
