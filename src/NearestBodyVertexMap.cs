using UnityEngine;

namespace ALPregnancy;

internal static partial class BellyVertexMorph
{
    // Owned by the overlay MeshRecord. OrigVerts baselines are replaced, never
    // mutated in place; renderer visibility and current deformed vertices are
    // not inputs to the rest-vertex correspondence.
    private sealed class NearestBodyVertexMap
    {
        private Vector3[] _overlayVertices;
        private Vector3[] _bodyVertices;
        private System.Numerics.Matrix4x4 _toBody;
        private int[] _indices;

        public int[] Get(Vector3[] overlayVertices, Vector3[] bodyVertices, Matrix4x4 toBody)
        {
            var mapping = MatrixBridge.ToManaged(toBody);
            // Numerics.Equals compares components exactly; Unity's approximate
            // matrix equality could miss a changed rest-space mapping.
            if (_indices != null && ReferenceEquals(_overlayVertices, overlayVertices)
                && ReferenceEquals(_bodyVertices, bodyVertices) && _toBody.Equals(mapping))
                return _indices;

            var bodySpaceVertices = new Vector3[overlayVertices.Length];
            for (int i = 0; i < bodySpaceVertices.Length; i++)
                bodySpaceVertices[i] = toBody.MultiplyPoint3x4(overlayVertices[i]);
            var indices = BuildNearestBodyVertCache(bodySpaceVertices, bodyVertices);

            // Publish only after a successful rebuild, so an exception cannot
            // associate the old result with new inputs.
            _overlayVertices = overlayVertices;
            _bodyVertices = bodyVertices;
            _toBody = mapping;
            _indices = indices;
            return indices;
        }
    }
}
