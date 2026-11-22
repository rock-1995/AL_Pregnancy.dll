using Character;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace ALPregnancy;

// Only continuous body morph controls and nipple color participate. Face, clothes,
// IDs, names and other card metadata are never transferred to a live character.
internal sealed class BodyChangeValues
{
    internal float[] Shape;
    internal float[] Extra;
    internal Color Nipple;
    internal static readonly string[] ExtraNames = {
        "bustSoftness", "bustWeight", "areolaSize", "upperArmSoftness",
        "bellySoftness", "waistSoftness", "hipSoftness", "thighSoftness"
    };

    internal static BodyChangeValues Read(HumanDataBody b) => Read(b, null);

    internal static BodyChangeValues Read(HumanDataBody b, BodyChangeValues reuse)
    {
        var source = b.shapeValueBody;
        var result = reuse ?? new BodyChangeValues();
        result.Resize(source.Length, 8);
        source.AsSpan().CopyTo(result.Shape);
        result.Extra[0] = b.bustSoftness; result.Extra[1] = b.bustWeight; result.Extra[2] = b.areolaSize;
        result.Extra[3] = b.upperArmSoftness; result.Extra[4] = b.bellySoftness; result.Extra[5] = b.waistSoftness;
        result.Extra[6] = b.hipSoftness; result.Extra[7] = b.thighSoftness; result.Nipple = b.nipColor;
        return result;
    }

    private void Resize(int shapeCount, int extraCount)
    {
        if (Shape == null || Shape.Length != shapeCount) Shape = new float[shapeCount];
        if (Extra == null || Extra.Length != extraCount) Extra = new float[extraCount];
    }

    internal void Validate()
    {
        bool valid = Shape != null && Shape.Length == (int)HumanBody.Define.BodyShapeIdx.HipSizeY + 1;
        if (valid) foreach (float value in Shape) valid &= float.IsFinite(value);
        if (valid) foreach (float value in Extra) valid &= float.IsFinite(value);
        if (valid) for (int i = 0; i < 4; i++) valid &= float.IsFinite(Nipple[i]);
        if (!valid)
            throw new InvalidDataException("Invalid body morph values in template card.");
    }

    internal static BodyChangeValues Difference(BodyChangeValues baseline, BodyChangeValues reference)
    {
        baseline.Validate(); reference.Validate();
        return new BodyChangeValues {
            Shape = reference.Shape.Zip(baseline.Shape, (r, b) => r - b).ToArray(),
            Extra = reference.Extra.Zip(baseline.Extra, (r, b) => r - b).ToArray(),
            Nipple = reference.Nipple - baseline.Nipple
        };
    }

    internal BodyChangeValues Apply(BodyChangeValues delta, double progress, int lower, int upper, float colorMaximum) =>
        Apply(delta, progress, lower, upper, colorMaximum, null);

    internal BodyChangeValues Apply(BodyChangeValues delta, double progress, int lower, int upper, float colorMaximum,
        BodyChangeValues reuse)
    {
        var result = reuse ?? new BodyChangeValues();
        result.Resize(Shape.Length, Extra.Length);
        for (int i = 0; i < Shape.Length; i++) result.Shape[i] = BodyChangeMath.Add(Shape[i], delta.Shape[i], progress, lower, upper);
        for (int i = 0; i < Extra.Length; i++) result.Extra[i] = BodyChangeMath.Add(Extra[i], delta.Extra[i], progress, lower, upper);
        result.Nipple = Nipple;
        double colorProgress = BodyChangeMath.ColorProgress(progress, colorMaximum);
        for (int i = 0; i < 4; i++)
            result.Nipple[i] = BodyChangeMath.Add(Nipple[i], delta.Nipple[i], colorProgress, lower, upper);
        return result;
    }

    internal bool Same(BodyChangeValues other)
    {
        if (other == null || !Shape.AsSpan().SequenceEqual(other.Shape) || !Extra.AsSpan().SequenceEqual(other.Extra)) return false;
        for (int i = 0; i < 4; i++) if (Nipple[i] != other.Nipple[i]) return false;
        return true;
    }

    internal void Write(HumanDataBody b)
    {
        Il2CppStructArray<float> ownedShape = null;
        Write(b, ref ownedShape);
    }

    internal void Write(HumanDataBody b, ref Il2CppStructArray<float> ownedShape)
    {
        // Own this array even if a future version of the game's Copy becomes shallow.
        // Reuse only the adapter's private array, never the array copied from the card.
        if (ownedShape == null || ownedShape.Length != Shape.Length) ownedShape = new Il2CppStructArray<float>(Shape);
        else Shape.AsSpan().CopyTo(ownedShape.AsSpan());
        b.shapeValueBody = ownedShape;
        b.bustSoftness = Extra[0]; b.bustWeight = Extra[1]; b.areolaSize = Extra[2];
        b.upperArmSoftness = Extra[3]; b.bellySoftness = Extra[4]; b.waistSoftness = Extra[5];
        b.hipSoftness = Extra[6]; b.thighSoftness = Extra[7]; b.nipColor = Nipple;
    }
}
