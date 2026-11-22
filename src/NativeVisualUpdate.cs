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
            bool wasPreview = PregnancyController.ManualPreviewActive;
            PregnancyController.AfterNativeUpdate(human);
            if (wasPreview && !PregnancyController.ManualPreviewActive) PregnancyRuntime.UpdateHumanVisual(human);
            // Both render paths consume the same customization values. No
            // pregnancy simulation or roster search is needed on stable frames.
            var custom = human.Data?.Custom;
            BodyChangeRuntime.ApplyAfterBelly(human, custom == null ? 0 : PregnancyAppearance.Get(custom).Body);
            BellyVertexMorph.UpdateVirtual(human);
            BellyVertexMorph.UpdateBodyCollision(human);
        }
        finally { MorphReadiness.ExitNativeBoundary(); }
    }
}
