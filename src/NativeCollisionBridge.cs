using System;

namespace ALPregnancy;

// Small, optional interface for native breast physics. It publishes only the
// belly input cache; ownership of liquid mesh submission stays in this plugin.
public static class NativeCollisionBridge
{
    public static void RefreshInput(SkinnedCollisionHelper helper)
        => BellyVertexMorph.RefreshNativeCollisionInput(helper);
}

internal static partial class BellyVertexMorph
{
    internal static void RefreshNativeCollisionInput(SkinnedCollisionHelper helper)
    {
        if (helper == null || !CollisionBindings.TryGetValue(helper.GetInstanceID(), out var binding)) return;
        if (!binding.Valid || !binding.Rig.Ready || binding.Rig.Failed)
            throw new InvalidOperationException("Pregnancy collision input is being rebuilt.");
        // Uses the last completed visual deformation and current carrier matrices.
        // Inactive liquid helpers otherwise retain an old pose. Do not activate
        // their GameObjects, call UpdateCollisionMesh, cook PhysX, or dirty Obi.
        UpdateCollisionInputs(binding);
    }
}
