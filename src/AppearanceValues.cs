using System;

namespace ALPregnancy;

/// <summary>The two character-specific appearance slider values.</summary>
public sealed class AppearanceValues
{
    private float _belly;
    private float _body;

    public float Belly { get => _belly; set => SetBelly(value); }
    public float Body { get => _body; set => SetBody(value); }

    public void SetBelly(float value) => _belly = Normalize(value, nameof(Belly));
    public void SetBody(float value) => _body = Normalize(value, nameof(Body));

    public AppearanceValues Clone() => new() { _belly = _belly, _body = _body };

    private static float Normalize(float value, string name)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(name, value, "Appearance slider values must be finite.");
        return Math.Clamp(value, 0f, 1f);
    }
}
