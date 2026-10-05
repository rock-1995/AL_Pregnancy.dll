using ALPregnancy;
using System.Text.Json;

internal static class ReleaseDefaultsRegression
{
    internal static void Run(Action<string, bool> check)
    {
        // This settings-only fixture is the approved release preset. No geometry or save data is included.
        using var preset = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "release-defaults.json")));
        var expected = preset.RootElement.GetProperty("vtx");
        using var actual = JsonDocument.Parse(JsonSerializer.Serialize(new VtxSettings()));
        check("Release preset keeps format 5 and start day 40", preset.RootElement.GetProperty("version").GetInt32() == 5 && preset.RootElement.GetProperty("startDay").GetInt32() == 40);
        check("Release defaults and release preset contain exactly 73 settings", expected.EnumerateObject().Count() == 73 && actual.RootElement.EnumerateObject().Count() == 73);
        foreach (var property in expected.EnumerateObject())
        {
            bool found = actual.RootElement.TryGetProperty(property.Name, out var value);
            bool equal = found && value.ValueKind == property.Value.ValueKind && (value.ValueKind == JsonValueKind.Number
                ? value.GetSingle() == property.Value.GetSingle()
                : value.GetRawText() == property.Value.GetRawText());
            check("Release default matches release preset: " + property.Name, equal);
        }
    }
}
