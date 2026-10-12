using Character;
using UnityEngine;

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

    internal static BodyChangeValues Read(HumanDataBody b) => new()
    {
        Shape = b.shapeValueBody.ToArray(),
        Extra = new[] { b.bustSoftness, b.bustWeight, b.areolaSize, b.upperArmSoftness,
            b.bellySoftness, b.waistSoftness, b.hipSoftness, b.thighSoftness },
        Nipple = b.nipColor
    };

    internal void Validate()
    {
        if (Shape == null || Shape.Length != (int)HumanBody.Define.BodyShapeIdx.HipSizeY + 1 ||
            Shape.Any(x => !float.IsFinite(x)) || Extra.Any(x => !float.IsFinite(x)) ||
            Enumerable.Range(0, 4).Any(i => !float.IsFinite(Nipple[i])))
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

    internal BodyChangeValues Apply(BodyChangeValues delta, double progress, int lower, int upper, float colorMaximum)
    {
        var result = new BodyChangeValues {
            Shape = Shape.Select((v, i) => BodyChangeMath.Add(v, delta.Shape[i], progress, lower, upper)).ToArray(),
            Extra = Extra.Select((v, i) => BodyChangeMath.Add(v, delta.Extra[i], progress, lower, upper)).ToArray(),
            Nipple = Nipple
        };
        double colorProgress = BodyChangeMath.ColorProgress(progress, colorMaximum);
        for (int i = 0; i < 4; i++)
            result.Nipple[i] = BodyChangeMath.Add(Nipple[i], delta.Nipple[i], colorProgress, lower, upper);
        return result;
    }

    internal bool Same(BodyChangeValues other) => other != null &&
        Shape.SequenceEqual(other.Shape) && Extra.SequenceEqual(other.Extra) &&
        Enumerable.Range(0, 4).All(i => Nipple[i] == other.Nipple[i]);

    internal void Write(HumanDataBody b)
    {
        // Own this array even if a future version of the game's Copy becomes shallow.
        b.shapeValueBody = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float>(Shape);
        b.bustSoftness = Extra[0]; b.bustWeight = Extra[1]; b.areolaSize = Extra[2];
        b.upperArmSoftness = Extra[3]; b.bellySoftness = Extra[4]; b.waistSoftness = Extra[5];
        b.hipSoftness = Extra[6]; b.thighSoftness = Extra[7]; b.nipColor = Nipple;
    }
}
