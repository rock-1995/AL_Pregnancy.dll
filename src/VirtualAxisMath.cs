using System.Numerics;

namespace ALPregnancy;

// Row-vector matrices throughout. No Unity pose or mesh access in this kernel.
internal static class VirtualAxisMath
{
    internal readonly record struct Axis(Vector3 Anchor, Vector3 Upper);
    internal readonly record struct Result(Matrix4x4 Transform, float AngleDegrees, float Pull, Vector3 Anchor, Vector3 FreeAxis, Vector3 TargetAxis);

    internal static Axis Reference(TorsoProfile torso,float stage,VtxSettings p)
    {
        var seed=BellyShape.Growth(torso,1f/3,p);
        var egg=BellyShape.Growth(torso,MathF.Max(stage,1f/3),p);
        // The initial occupied centre is a material anchor, not the growing egg's pole.
        var anchor=new Vector3(0,(seed.Bottom+seed.Top)*.5f+p.AxisAnchorY*torso.Span,seed.AnteriorOffset+p.AxisAnchorZ*torso.Span);
        // Centroid of the upper half of a solid ellipsoid: 3/8 of its vertical semiaxis.
        var upper=new Vector3(0,(egg.Bottom+egg.Top)*.5f+(egg.Top-egg.Bottom)*.1875f,egg.AnteriorOffset);
        if(Vector3.DistanceSquared(anchor,upper)<torso.Span*torso.Span*.0025f)upper=anchor+Vector3.UnitY*torso.Span*.05f;
        return new Axis(anchor,upper);
    }

    internal static Result Evaluate(Axis rest,Matrix4x4 pelvis,Matrix4x4 spine,VtxSettings p)
    {
        var anchor=Vector3.Transform(rest.Anchor,pelvis);
        var free=Unit(Vector3.TransformNormal(rest.Upper-rest.Anchor,pelvis),Vector3.UnitY);
        var target=Unit(Vector3.Transform(rest.Upper,spine)-anchor,free);
        float cosine=Math.Clamp(Vector3.Dot(free,target),-1,1);
        float angle=MathF.Acos(cosine),degrees=angle*(180/MathF.PI);
        float low=Math.Clamp(p.AxisPullLow,0,1),high=MathF.Max(low,Math.Clamp(p.AxisPullHigh,0,1));
        float pull=low+(high-low)*BellyShape.Smooth(degrees/MathF.Max(1,p.AxisPullAngle));
        var turnAxis=Vector3.Cross(free,target);
        if(turnAxis.LengthSquared()<1e-10f)
        {
            if(cosine>0)return new Result(pelvis,degrees,pull,anchor,free,target);
            // Antiparallel fallback rotates with the character, not with world axes.
            var right=Vector3.TransformNormal(Vector3.UnitX,pelvis);
            turnAxis=right-free*Vector3.Dot(right,free);
        }
        var turn=Matrix4x4.CreateFromAxisAngle(Unit(turnAxis,Vector3.UnitX),angle*pull);
        var matrix=pelvis*Matrix4x4.CreateTranslation(-anchor)*turn*Matrix4x4.CreateTranslation(anchor);
        return new Result(matrix,degrees,pull,anchor,free,target);
    }

    internal static float Weight(float displacement,float span,VtxSettings p)
    {
        float start=Math.Clamp(p.AxisBlendStart,0,.5f),end=MathF.Max(start+.05f,Math.Clamp(p.AxisBlendFull,.005f,1));
        return Math.Clamp(p.VirtualAxisStrength,0,1)*BellyShape.Smooth((displacement/MathF.Max(span,1e-5f)-start)/(end-start));
    }

    // A displacement threshold alone can switch the entire lower wall to the
    // pelvis carrier within a few tightly spaced rows. Retain native attachment
    // across a continuous anatomical length, reaching full virtual support near
    // the navel. This is a rest/material coordinate, shared with clothing, and
    // does not depend on pose angle, mesh density, or world orientation.
    internal static float SurfaceWeight(float displacement, float materialY, TorsoProfile torso, VtxSettings p)
    {
        float start = torso.PelvicFloor + Parameter(p.LowerTransitionStart, .02f, -.50f, .75f) * torso.Span;
        float u = Math.Clamp((materialY - start) / MathF.Max(torso.Span * Parameter(p.LowerTransitionWidth, .60f, .02f, 1.50f), 1e-5f), 0, 1);
        float attachment = LowerAttachment(u, p.LowerTransitionBias);
        return Weight(displacement, torso.Span, p) * attachment;
    }

    internal static float Parameter(float value, float fallback, float min, float max)
        => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    internal static float UpperStart(TorsoProfile torso, VtxSettings p)
        => torso.Navel + Parameter(p.UpperTransitionStart, .15f, -.50f, 1f) * torso.Span;

    internal static float UpperRelease(float u, float bias) => 1 - LowerAttachment(u, -bias);

    // Built into skin weights on a shape rebuild, never evaluated per pose.
    // Carry the deformation at the upper band's entrance up the same material
    // meridian, then release it by HEIGHT. Do not multiply a second fade into
    // the current vertex's already-falling displacement weight.
    internal static float MaterialSurfaceWeight(float displacement, Vector3 original,
        TorsoProfile torso, float stage, VtxSettings p, float influence = 1f)
    {
        if (stage <= 0 || !float.IsFinite(displacement) || displacement <= 0 || influence <= 0) return 0;
        float baseline = SurfaceWeight(displacement, original.Y, torso, p);
        float start = UpperStart(torso, p);
        if (original.Y <= start) return baseline;
        float width = Parameter(p.UpperTransitionWidth, .55f, .02f, 1.50f) * torso.Span;
        float u = (original.Y - start) / MathF.Max(width, 1e-5f);
        if (u >= 1 || displacement <= 0 || stage <= 0 || p.VirtualAxisStrength <= 0) return 0;
        float angle = MathF.Atan2(original.X, original.Z - torso.AxisAt(original.Y));
        if (MathF.Abs(angle) >= MathF.PI * .5f) return 0;
        float sn = MathF.Sin(angle), cs = MathF.Cos(angle);
        float Radius(float y)
        {
            float w = MathF.Max(torso.Span * .01f, torso.WidthAt(y));
            float d = MathF.Max(torso.Span * .01f, (torso.FrontAt(y) - torso.BackAt(y)) * .5f);
            return 1 / MathF.Sqrt(sn * sn / (w * w) + cs * cs / (d * d));
        }
        // Preserve the original surface's radial detail while transporting it
        // to the anchor height; identical material points need identical rules.
        float radial = MathF.Sqrt(original.X * original.X + MathF.Pow(original.Z - torso.AxisAt(original.Y), 2));
        float radius = MathF.Max(0, Radius(start) + radial - Radius(original.Y));
        var anchor = new Vector3(radius * sn, start, torso.AxisAt(start) + radius * cs);
        float anchorDelta = Vector3.Distance(anchor, BellyShape.Deform(anchor, torso, stage, p)) * Parameter(influence, 1, 0, 1);
        float entry = SurfaceWeight(anchorDelta, start, torso, p);
        float target = entry * UpperRelease(u, p.UpperTransitionBias);
        float activation = BellyShape.Smooth(displacement / MathF.Max(torso.Span * Parameter(p.UpperTransitionActivation, .02f, .001f, .15f), 1e-5f));
        target *= activation;
        // A short zero-slope join preserves the lower formula exactly below
        // the chosen start, even on detailed skin or overlapping user bands.
        float join = BellyShape.Smooth(u / Parameter(p.UpperTransitionJoin, .20f, .02f, 1f));
        return baseline + (target - baseline) * join;
    }

    // Bias the interior of the same material interval, preserving both endpoint
    // values and zero endpoint slopes. The explicit zero branch keeps old
    // settings bit-for-bit identical, including configs without the new field.
    internal static float LowerAttachment(float u, float bias)
    {
        u = Math.Clamp(u, 0, 1);
        bias = float.IsFinite(bias) ? Math.Clamp(bias, -2, 2) : 0;
        if (bias != 0)
        {
            float scale = MathF.Pow(2, bias);
            u /= u + (1 - u) * scale;
        }
        return u * u * (3 - 2 * u);
    }

    // Historical replay helper only. Runtime uses MaterialSurfaceWeight.
    // Upper skin can fold when the displacement-based blend falls sharply
    // between adjacent rows. Bias that existing blend, rather than forcing it
    // to zero at the ribs (which can collapse the expanded upper wall).
    // Fade this control in above BOTH the navel and the lower transition.
    internal static float UpperAttachment(float weight, float materialY, TorsoProfile torso, float bias, float ceiling = 1f)
    {
        bias = float.IsFinite(bias) ? Math.Clamp(bias, -2, 2) : 0;
        if (bias == 0 || weight <= 0 || weight >= ceiling) return weight;
        float start = MathF.Max(torso.Navel + .15f * torso.Span, torso.PelvicFloor + .62f * torso.Span);
        float extent = torso.Ribs - start;
        if (extent <= 1e-5f || materialY <= start) return weight;
        float u = Math.Clamp((materialY - start) / extent, 0, 1);
        float mask = u * u * (3 - 2 * u);
        float remapped = ceiling * weight / (weight + (ceiling - weight) * MathF.Pow(2, bias));
        return weight + (remapped - weight) * mask;
    }

    // Reuse an existing pelvis Transform; its extra bind matrix supplies the
    // independent virtual transform. No Transform or hierarchy bone is created.
    internal static bool PaletteBind(Matrix4x4 meshToReference,Matrix4x4 virtualToWorld,Matrix4x4 carrierToWorld,out Matrix4x4 bind)
    {
        bind=default;
        if(!Matrix4x4.Invert(carrierToWorld,out var inverse))return false;
        bind=meshToReference*virtualToWorld*inverse;
        return Finite(bind);
    }
    internal static Vector3 Unit(Vector3 v,Vector3 fallback)=>v.LengthSquared()>1e-12f?Vector3.Normalize(v):fallback;
    internal static bool Finite(Matrix4x4 m)=>float.IsFinite(m.M11+m.M12+m.M13+m.M14+m.M21+m.M22+m.M23+m.M24+m.M31+m.M32+m.M33+m.M34+m.M41+m.M42+m.M43+m.M44);
}
