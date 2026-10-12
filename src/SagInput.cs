using System.Globalization;

namespace ALPregnancy;

// Slider limits describe the recommendation only; text accepts any finite float.
internal static class SagInput
{
    internal static bool TryRead(string text, out float value)
        => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
    internal static float SliderPosition(float value) => Math.Clamp(value, 0, 2);
    internal static float FromSlider(float current, float position)
        => position == SliderPosition(current) ? current : position;
}
