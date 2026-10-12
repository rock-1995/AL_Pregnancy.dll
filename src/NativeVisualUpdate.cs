using Character;

namespace ALPregnancy;

// Complete one actor synchronously after the game has made its meshes ready.
// Static belly vertices precede native body customization; virtual bindings,
// pose palettes and collision then consume the final customized skeleton.
internal static class NativeVisualUpdate
{
    internal static void Complete(Human human)
    {
        if (human == null || human.Disposed || !human.HiPoly || human.Sex != 1) return;
        if (!MorphReadiness.EnterNativeBoundary(human)) return;
        try
        {
            PregnancyRuntime.UpdateHumanVisual(human);
            PregnancyController.AfterNativeUpdate(human);
            BodyChangeRuntime.ApplyAfterBelly(human, PregnancyRuntime.FindVisualRecord(human));
            BellyVertexMorph.UpdateVirtual(human);
            BellyVertexMorph.UpdateBodyCollision(human);
        }
        finally { MorphReadiness.ExitNativeBoundary(); }
    }
}
