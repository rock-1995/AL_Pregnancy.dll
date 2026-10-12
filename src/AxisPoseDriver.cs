using Character;
using UnityEngine;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace ALPregnancy;

// The palette carrier may be a skin bone, but local soft-tissue physics must
// not drive the whole abdominal axis. Keep its live customization below the
// spring and the live animated skeleton above it; replace only spring motion.
internal sealed class AxisPoseDriver
{
    private sealed class SpringRest
    {
        internal Transform Root;
        internal NVector Position;
        internal NQuaternion Rotation;
        internal SpringRest(Transform root, NVector position, NQuaternion rotation)
        { Root = root; Position = position; Rotation = rotation; }
    }
    private readonly Transform skin;
    private readonly List<SpringRest> springs = new();
    private AxisPoseDriver(Transform bone) { skin = bone; }
    internal string[] RemovedPhysicsRoots => springs.Select(s => s.Root.name).ToArray();

    internal static AxisPoseDriver Capture(Human human, Transform skin, HumanMagicaSpringSetting.Type type)
    {
        var result = new AxisPoseDriver(skin);
        var entries = human?.Body?.magicaSpringSetting?.Get(type);
        if (entries == null) return result;
        var byId = new Dictionary<int, SpringRest>();
        foreach (var entry in entries)
        {
            var spring = entry?.Spring;
            var roots = spring?.SerializeData?.rootBones;
            if (roots == null) continue;
            foreach (var root in roots)
            {
                if (root == null || byId.ContainsKey(root.GetInstanceID())) continue;
                bool ancestor = false;
                for (var p = skin; p != null; p = p.parent)
                    if (p.GetInstanceID() == root.GetInstanceID()) { ancestor = true; break; }
                if (!ancestor) continue;
                var setups = spring.GetSerializeData2()?.initData?.clothSetupDataList;
                MagicaCloth2.RenderSetupSerializeData setup = null;
                int index = -1;
                if (setups != null)
                    foreach (var candidate in setups)
                    {
                        if (candidate?.transformArray == null) continue;
                        for (int i = 0; i < candidate.transformArray.Length; i++)
                            if (candidate.transformArray[i] != null && candidate.transformArray[i].GetInstanceID() == root.GetInstanceID())
                            { setup = candidate; index = i; break; }
                        if (index >= 0) break;
                    }
                if (index < 0) throw new InvalidOperationException("Missing spring rest transform for axis driver: " + root.name);
                var position = setup.transformLocalPositions[index];
                var rotation = setup.transformLocalRotations[index].value;
                byId.Add(root.GetInstanceID(), new(root, new(position.x, position.y, position.z),
                    new(rotation.x, rotation.y, rotation.z, rotation.w)));
            }
        }
        // Inner correction first, so nested physical roots are removed exactly once.
        for (var p = skin; p != null; p = p.parent)
            if (byId.TryGetValue(p.GetInstanceID(), out var rest)) result.springs.Add(rest);
        return result;
    }

    internal NMatrix World()
    {
        var world = MatrixBridge.ToManaged(skin.localToWorldMatrix);
        foreach (var rest in springs)
        {
            var root = rest.Root;
            if (root == null || !NMatrix.Invert(MatrixBridge.ToManaged(root.localToWorldMatrix), out var inverse))
                throw new InvalidOperationException("Invalid soft-tissue transform in axis driver.");
            var scale = root.localScale;
            var local = NMatrix.CreateScale(scale.x, scale.y, scale.z)
                * NMatrix.CreateFromQuaternion(rest.Rotation) * NMatrix.CreateTranslation(rest.Position);
            var parent = root.parent == null ? NMatrix.Identity : MatrixBridge.ToManaged(root.parent.localToWorldMatrix);
            world = world * inverse * local * parent;
        }
        return world;
    }
}
