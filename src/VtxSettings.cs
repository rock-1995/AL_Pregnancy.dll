using System.Text.Json.Serialization;

namespace ALPregnancy
{
    // ── Vertex-mode parameters ───────────────────────────────────────────
    public class VtxSettings
    {
        // Defaults captured from the user's 2026-10-06 00:33:28 v0.2.23 diagnostic.
        // Keep full precision so resetting reproduces the accepted settings.
        [JsonPropertyName("growthFullness")] public float GrowthFullness { get; set; } = 1.116265f;
        [JsonPropertyName("growthWidth")] public float GrowthWidth { get; set; } = 1.1927711f;
        [JsonPropertyName("upperReach")] public float UpperReach { get; set; } = 1.1990964f;
        [JsonPropertyName("lateSettle")] public float LateSettle { get; set; } = 0.502008f;
        [JsonPropertyName("verticalRange")] public float VerticalRange { get; set; } = 0.993976f;
        [JsonPropertyName("wallSmoothing")] public float WallSmoothing { get; set; } = 2f;
        [JsonPropertyName("sagStrength")] public float SagStrength { get; set; } = 0.95983934f;
        [JsonPropertyName("midVolume")] public float MidVolume { get; set; } = 1.1551205f;
        [JsonPropertyName("lowerPoleLift")] public float LowerPoleLift { get; set; } = 1.0240964f;
        [JsonPropertyName("skinClearance")] public float SkinClearance { get; set; } = 0.19116466f;
        // Pose equation: a virtual matrix palette entry, without hierarchy bones.
        [JsonPropertyName("virtualAxisStrength")] public float VirtualAxisStrength { get; set; } = 1f;
        [JsonPropertyName("axisBlendStart")] public float AxisBlendStart { get; set; } = 0.02f;
        [JsonPropertyName("axisBlendFull")] public float AxisBlendFull { get; set; } = 0.25f;
        // Rest-height offsets and widths are normalised by torso span.
        [JsonPropertyName("lowerTransitionStart")] public float LowerTransitionStart { get; set; } = .02f;
        [JsonPropertyName("lowerTransitionWidth")] public float LowerTransitionWidth { get; set; } = .60f;
        // Negative: earlier virtual attachment; positive: later.
        [JsonPropertyName("lowerTransitionBias")] public float LowerTransitionBias { get; set; } = 0f;
        // Upper start is relative to the original waist/growth datum (TorsoProfile.Navel).
        [JsonPropertyName("upperTransitionStart")] public float UpperTransitionStart { get; set; } = 0.10542166f;
        [JsonPropertyName("upperTransitionWidth")] public float UpperTransitionWidth { get; set; } = 0.5965462f;
        // Negative: later release to native skinning; positive: earlier release.
        [JsonPropertyName("upperTransitionBias")] public float UpperTransitionBias { get; set; } = 0f;
        [JsonPropertyName("upperTransitionJoin")] public float UpperTransitionJoin { get; set; } = .20f;
        [JsonPropertyName("upperTransitionActivation")] public float UpperTransitionActivation { get; set; } = .02f;
        // Retained for loading old presets only. The per-frame support pass is retired.
        [JsonPropertyName("upperFoldSupport")] public float UpperFoldSupport { get; set; } = 0f;
        // Trial switches: height transitions remain active independently.
        [JsonPropertyName("breastExclusionEnabled")] public bool BreastExclusionEnabled { get; set; } = false;
        [JsonPropertyName("upperBoneFilterEnabled")] public bool UpperBoneFilterEnabled { get; set; } = false;
        [JsonPropertyName("upperFieldFadeEnabled")] public bool UpperFieldFadeEnabled { get; set; } = false;
        [JsonPropertyName("axisPullLow")] public float AxisPullLow { get; set; } = 0.15f;
        [JsonPropertyName("axisPullHigh")] public float AxisPullHigh { get; set; } = 0.8f;
        [JsonPropertyName("axisPullAngle")] public float AxisPullAngle { get; set; } = 60f;
        [JsonPropertyName("axisAnchorY")] public float AxisAnchorY { get; set; } = 0f;
        [JsonPropertyName("axisAnchorZ")] public float AxisAnchorZ { get; set; } = 0f;
        [JsonPropertyName("skinShadingSmoothing")] public float SkinShadingSmoothing { get; set; } = 0.9859438f;
        [JsonPropertyName("navelPreviewFull")] public bool NavelPreviewFull { get; set; } = true;

        // Only the original navel patch is reshaped; never a second landmark.
        [JsonPropertyName("navelEversion")] public float NavelEversion { get; set; } = 1.2971888f;
        [JsonPropertyName("navelStart")] public float NavelStart { get; set; } = 0.6f;
        [JsonPropertyName("navelHeight")] public float NavelHeight { get; set; } = 0.008f;
        [JsonPropertyName("navelRadius")] public float NavelRadius { get; set; } = 0.046194777f;
        [JsonPropertyName("navelProportion")] public float NavelProportion { get; set; } = 0.5461847f;
        // Legacy shape parameters below are ignored by the growth kernel.
        // Their original length/offset units were normalised by the character's
        // pelvis-to-spine bone distance (boneLen).  1.0 = one boneLen unit.
        // This makes the same settings produce proportionally identical results
        // on characters of any height or body scale.

        /// <summary>Lerp between pelvis (0) and spine (1) to place the belly center.</summary>
        [JsonPropertyName("spineLerpT")]      public float SpineLerpT    { get; set; } = 0.5f;
        /// <summary>Global size multiplier applied to all radii (1 = normal).</summary>
        [JsonPropertyName("inflationSize")]   public float InflationSize { get; set; } = 4f;
        /// <summary>Additional up-axis offset of the belly centre, normalised by boneLen.</summary>
        [JsonPropertyName("moveY")]           public float MoveY         { get; set; } = -0.656f;
        /// <summary>Additional forward-axis offset of the belly centre, normalised by boneLen.</summary>
        [JsonPropertyName("moveZ")]           public float MoveZ         { get; set; } = -1.125f;
        /// <summary>Ellipsoid half-radius left/right, normalised by boneLen.</summary>
        [JsonPropertyName("radiusSide")]      public float RadiusSide    { get; set; } = 2.025f;
        /// <summary>Ellipsoid half-radius forward (front face), normalised by boneLen.</summary>
        [JsonPropertyName("radiusFront")]     public float RadiusFront   { get; set; } = 2.311f;
        /// <summary>Ellipsoid half-radius backward (back face), normalised by boneLen.</summary>
        [JsonPropertyName("radiusBack")]      public float RadiusBack    { get; set; } = 0f;
        /// <summary>Ellipsoid half-radius upward, normalised by boneLen.</summary>
        [JsonPropertyName("radiusUp")]        public float RadiusUp      { get; set; } = 2.704f;
        /// <summary>Ellipsoid half-radius downward, normalised by boneLen.</summary>
        [JsonPropertyName("radiusDown")]      public float RadiusDown    { get; set; } = 2.015f;
        /// <summary>Extra scale along the belly's right axis.</summary>
        [JsonPropertyName("stretchX")]        public float StretchX      { get; set; } = 0f;
        /// <summary>Extra scale along the belly's up axis.</summary>
        [JsonPropertyName("stretchY")]        public float StretchY      { get; set; } = 0f;
        /// <summary>Extra scale along the belly's forward axis.</summary>
        [JsonPropertyName("stretchZ")]        public float StretchZ      { get; set; } = 0.125f;
        /// <summary>Shift the deformed belly upward (+) or downward (−), normalised by boneLen.</summary>
        [JsonPropertyName("shiftY")]          public float ShiftY        { get; set; } = 0f;
        /// <summary>Shift the deformed belly forward (+) or backward (−), normalised by boneLen.</summary>
        [JsonPropertyName("shiftZ")]          public float ShiftZ        { get; set; } = 0f;
        /// <summary>Gravity-style downward pull on the front face. 0 = round, 1 = heavy sag.</summary>
        [JsonPropertyName("drop")]            public float Drop          { get; set; } = -0.5f;
        /// <summary>Vertical taper: >0 = narrower at top, <0 = wider at top.</summary>
        [JsonPropertyName("taperY")]          public float TaperY        { get; set; } = 0f;
        /// <summary>Depth taper: >0 = narrower at front, <0 = wider at front.</summary>
        [JsonPropertyName("taperZ")]          public float TaperZ        { get; set; } = 0.266f;
        /// <summary>Blend toward sphere (positive) or sharpen ellipsoid (negative). Range [-1, 1].</summary>
        [JsonPropertyName("roundness")]       public float Roundness     { get; set; } = 0.219f;
        /// <summary>
        /// Width of the smooth falloff zone at the belly boundary.
        /// 0 = falloff only at ellipsoid surface (sharpest edge).
        /// 1 = falloff begins at 50% of the radius (very soft, feathered edge).
        /// </summary>
        [JsonPropertyName("edgeSmooth")]      public float EdgeSmooth    { get; set; } = 1f;
        /// <summary>Depth of the fat-fold crease under the belly. 0 = none.</summary>
        [JsonPropertyName("fatFold")]         public float FatFold       { get; set; } = 0f;
        /// <summary>Vertical position of the fat-fold, normalised by RadiusDown (0 = center, 1 = bottom).</summary>
        [JsonPropertyName("fatFoldHeight")]   public float FatFoldHeight { get; set; } = 0f;
        /// <summary>Width of the fat-fold Gaussian, normalised by RadiusDown.</summary>
        [JsonPropertyName("fatFoldGap")]      public float FatFoldGap    { get; set; } = 0.05f;

        // ── Back-face limiter ─────────────────────────────────────────────
        /// <summary>
        /// Normalised depth of the back-cut plane (0=disabled, 1=cut at belly center).
        /// Vertices behind  fwD = -BackLimit*rB  have their deformation reduced.
        /// </summary>
        [JsonPropertyName("backLimit")]    public float BackLimit    { get; set; } = 0f;
        /// <summary>How much to reduce deformation in the back zone. 0=none, 1=full cut.</summary>
        [JsonPropertyName("backStrength")] public float BackStrength { get; set; } = 0f;
        /// <summary>
        /// Width of the smooth transition zone, normalised by rB.
        /// Points within this distance from the cut plane are blended gradually.
        /// </summary>
        [JsonPropertyName("backSmooth")]   public float BackSmooth   { get; set; } = 0f;

        // ── Breast guard ──────────────────────────────────────────────────
        /// <summary>
        /// Strength of the breast-guard restore step.
        /// Vertices weighted to breast bones (mune/bust) are lerped back toward their
        /// original positions by quinticSmooth(boneWeight × BreastGuardStrength).
        /// 0 = no guard; 1 = default (only full breast weighting is fully restored).
        /// Values above 1 guard more aggressively.
        /// </summary>
        [JsonPropertyName("breastGuard")] public float BreastGuardStrength { get; set; } = 1f;

        /// <summary>Fraction of longitudinal skin motion retained; 0 keeps navel height, 1 allows full radial stretch.</summary>
        [JsonPropertyName("verticalMotion")] public float VerticalMotion { get; set; } = 0.15f;

        // ── Clothing displacement multiplier ─────────────────────────────
        /// <summary>
        /// Clothing-only multiplier applied to the final deformation vector.
        /// 1.00 = same as body displacement; 1.01 = one percent more displacement.
        /// </summary>
        [JsonPropertyName("clothOffset")] public float ClothOffset { get; set; } = 1.01f;
        [JsonPropertyName("clothTopMult")] public float ClothTopMult { get; set; } = 1.01f;
        [JsonPropertyName("clothBotMult")] public float ClothBotMult { get; set; } = 1.01f;
        [JsonPropertyName("clothBraMult")] public float ClothBraMult { get; set; } = 1.01f;
        [JsonPropertyName("clothShortsMult")] public float ClothShortsMult { get; set; } = 1.01f;
        [JsonPropertyName("clothPanstMult")] public float ClothPanstMult { get; set; } = 1.01f;
        [JsonPropertyName("clothOtherMult")] public float ClothOtherMult { get; set; } = 1.01f;
        /// <summary>
        /// Clothing distortion detection threshold.  Difference between an edge
        /// vertex displacement and its more-central neighbour, normalised by boneLen.
        /// 0 disables the repair pass.
        /// </summary>
        [JsonPropertyName("clothDistortThreshold")] public float ClothDistortThreshold { get; set; } = 1.2f;
        /// <summary>
        /// Maximum displacement difference for a neighbour to be considered a normal
        /// replacement sample for a distorted clothing vertex, normalised by boneLen.
        /// </summary>
        [JsonPropertyName("clothDistortNeighborDiff")] public float ClothDistortNeighborDiff { get; set; } = 0.45f;
    }

}
