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
        float low=p.AxisPullLow,high=p.AxisPullHigh;
        float pull=low+(high-low)*BellyShape.Smooth(degrees/MathF.Max(1e-6f,MathF.Abs(p.AxisPullAngle)));
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
        float start=p.AxisBlendStart,end=p.AxisBlendFull;
        return p.VirtualAxisStrength*BellyShape.Smooth((displacement/MathF.Max(span,1e-5f)-start)/NonZero(end-start));
    }

    // A displacement threshold alone can switch the entire lower wall to the
    // pelvis carrier within a few tightly spaced rows. Retain native attachment
    // across a continuous anatomical length, reaching full virtual support near
    // the navel. This is a rest/material coordinate, shared with clothing, and
    // does not depend on pose angle, mesh density, or world orientation.
    internal static float SurfaceWeight(float displacement, float materialY, TorsoProfile torso, VtxSettings p)
    {
        float start = torso.PelvicFloor + Parameter(p.LowerTransitionStart, .02f) * torso.Span;
        float u = Math.Clamp((materialY - start) / NonZero(torso.Span * Parameter(p.LowerTransitionWidth, .60f)), 0, 1);
        float attachment = LowerAttachment(u, p.LowerTransitionBias);
        return Weight(displacement, torso.Span, p) * attachment;
    }

    // Only a numerical singularity guard, never a recommended-range clamp.
    internal static float NonZero(float value) => MathF.Abs(value)<1e-6f ? (value<0 ? -1e-6f : 1e-6f) : value;

    internal static float Parameter(float value, float fallback)
        => float.IsFinite(value) ? value : fallback;

    internal static float UpperStart(TorsoProfile torso, VtxSettings p)
        => torso.Navel + Parameter(p.UpperTransitionStart, .15f) * torso.Span;

    internal static float UpperRelease(float u, float bias) => 1 - LowerAttachment(u, -bias);

    // Built into skin weights on a shape rebuild, never evaluated per pose.
    // Carry the deformation at the upper band's entrance up the same material
    // meridian, then release it by HEIGHT. Do not multiply a second fade into
    // the current vertex's already-falling displacement weight.
    internal static float MaterialSurfaceWeight(float displacement, Vector3 original,
        TorsoProfile torso, float stage, VtxSettings p, float influence = 1f)
    {
        if (stage <= 0 || !float.IsFinite(displacement) || displacement <= 0 || influence <= 0) return 0;
        float materialY = HeightBoundary.Coordinate(original, torso, p);
        float baseline = SurfaceWeight(displacement, materialY, torso, p);
        float start = UpperStart(torso, p);
        if (materialY <= start) return baseline;
        float width = Parameter(p.UpperTransitionWidth, .55f) * torso.Span;
        float u = (materialY - start) / NonZero(width);
        if (u >= 1 || displacement <= 0 || stage <= 0 || p.VirtualAxisStrength <= 0) return 0;
        float angle = MathF.Atan2(original.X, original.Z - torso.AxisAt(original.Y));
        if (MathF.Abs(angle) >= MathF.PI * .5f) return 0;
        float anchorHeight = HeightBoundary.Height(start, angle, torso, p);
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
        float radius = MathF.Max(0, Radius(anchorHeight) + radial - Radius(original.Y));
        var anchor = new Vector3(radius * sn, anchorHeight, torso.AxisAt(anchorHeight) + radius * cs);
        float anchorDelta = Vector3.Distance(anchor, BellyShape.Deform(anchor, torso, stage, p)) * Parameter(influence, 1);
        float entry = SurfaceWeight(anchorDelta, start, torso, p);
        float target = entry * UpperRelease(u, p.UpperTransitionBias);
        float activation = BellyShape.Smooth(displacement / NonZero(torso.Span * Parameter(p.UpperTransitionActivation, .02f)));
        target *= activation;
        // A short zero-slope join preserves the lower formula exactly below
        // the chosen start, even on detailed skin or overlapping user bands.
        float join = BellyShape.Smooth(u / NonZero(Parameter(p.UpperTransitionJoin, .20f)));
        return baseline + (target - baseline) * join;
    }

    // Bias the interior of the same material interval, preserving both endpoint
    // values and zero endpoint slopes. The explicit zero branch keeps old
    // settings bit-for-bit identical, including configs without the new field.
    internal static float LowerAttachment(float u, float bias)
    {
        u = Math.Clamp(u, 0, 1);
        bias = float.IsFinite(bias) ? bias : 0;
        if (bias != 0)
        {
            if(u<=0 || u>=1)return u;
            if(bias>=0){float inverse=MathF.Pow(2,-bias);u=u*inverse/(1-u+u*inverse);}
            else u/=u+(1-u)*MathF.Pow(2,bias);
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
        bias = float.IsFinite(bias) ? bias : 0;
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

// One rest-space latitude for every height-limited deformation/attachment band.
// Central heights stay as configured. Both sides approach the same fixed belly
// centre: upper contours arch upward, lower contours arch downward. The centre
// uses the full-term envelope so advancing pregnancy cannot slide the bands.
internal static class HeightBoundary
{
    private readonly record struct Key(float Reach, float Lift, float Range, float Settle);
    private sealed class Cache { internal Key Key; internal float Center; internal bool Ready; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TorsoProfile, Cache> Centers = new();
    internal static float Center(TorsoProfile torso, VtxSettings p)
    {
        if (!Centers.TryGetValue(torso, out var cache)) { cache = new Cache(); Centers.Add(torso, cache); }
        var key = new Key(p.UpperReach, p.LowerPoleLift, p.VerticalRange, p.LateSettle);
        if (!cache.Ready || cache.Key != key)
        {
            var full = BellyShape.Growth(torso, 1, p);
            cache.Center = (full.Bottom + full.Top) * .5f;
            cache.Key = key; cache.Ready = true;
        }
        return cache.Center;
    }
    internal static float Scale(float angle, float amount)
    {
        amount = float.IsFinite(amount) ? MathF.Max(0, amount) : .35f;
        float side = MathF.Sin(angle);
        return MathF.Max(1e-6f, 1 / (1 + amount * side * side));
    }
    internal static float Coordinate(float y, float angle, float center, float amount)
    {
        float scale = Scale(angle, amount);
        return scale == 1 ? y : center + (y - center) / scale;
    }
    internal static float Coordinate(Vector3 original, TorsoProfile torso, VtxSettings p)
        => Coordinate(original.Y, MathF.Atan2(original.X, original.Z - torso.AxisAt(original.Y)), Center(torso, p), p.HeightBoundaryArc);
    internal static float Height(float level, float angle, TorsoProfile torso, VtxSettings p)
    {
        float scale = Scale(angle, p.HeightBoundaryArc);
        if (scale == 1) return level;
        float center = Center(torso, p);
        return center + (level - center) * scale;
    }
}
