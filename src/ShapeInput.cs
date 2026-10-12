using System.Globalization;

namespace ALPregnancy;

// The thumb has a recommendation range; the stored number has no such bounds.
internal sealed class ShapeInput
{
    internal string Text = "";
    private float _last = float.NaN;
    internal void Synchronize(float value)
    {
        if(value==_last)return;
        _last=value;Text=value.ToString("R",CultureInfo.InvariantCulture);
    }
    internal static bool TryRead(string text, out float value)
        => float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value) && float.IsFinite(value);
    internal static float SliderPosition(float value,float min,float max) => Math.Clamp(value,min,max);
    internal static float FromSlider(float current,float position,float min,float max)
        => position==SliderPosition(current,min,max) ? current : position;
}
