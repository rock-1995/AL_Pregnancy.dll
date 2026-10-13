using ALPregnancy;
using System.Text.Json;

internal static class ReleaseDefaultsRegression
{
    internal static void Run(Action<string, bool> check)
    {
        using var capture=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"user-defaults-0.2.40.json")));
        using var defaults=JsonDocument.Parse(JsonSerializer.Serialize(new VtxSettings()));
        using var preset=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"release-defaults.json")));
        var release=preset.RootElement.GetProperty("vtx");
        check("All 81 exported parameters become code and file defaults", capture.RootElement.EnumerateObject().Count()==81 && defaults.RootElement.EnumerateObject().Count()==81 && release.EnumerateObject().Count()==81);
        foreach(var field in capture.RootElement.EnumerateObject())
        {
            bool Equal(JsonElement item)=>item.ValueKind==field.Value.ValueKind && (item.ValueKind==JsonValueKind.Number ? item.GetSingle()==field.Value.GetSingle() : item.GetRawText()==field.Value.GetRawText());
            check("Code and preset match the 07:09:10 user export: "+field.Name, defaults.RootElement.TryGetProperty(field.Name,out var code)&&Equal(code)&&release.TryGetProperty(field.Name,out var file)&&Equal(file));
        }
        check("Default preset preserves format and start day",preset.RootElement.GetProperty("version").GetInt32()==5 && preset.RootElement.GetProperty("startDay").GetInt32()==40);
        using var oldPreset=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"previous-defaults-0.2.34.json")));
        var loaded=JsonSerializer.Deserialize<VtxSettings>(oldPreset.RootElement.GetProperty("vtx").GetRawText());
        using var roundTrip=JsonDocument.Parse(JsonSerializer.Serialize(loaded));
        check("Existing explicitly saved settings are not overwritten",oldPreset.RootElement.GetProperty("vtx").EnumerateObject().All(field=>roundTrip.RootElement.GetProperty(field.Name).GetRawText()==field.Value.GetRawText()));
    }
}
