using System.Security.Cryptography;
using System.Text.Json;

namespace ALPregnancy;

public sealed class PregnancySaveEnvelope
{
    public int Version { get; set; } = 3;
    public string NativeHash { get; set; } = "";
    public string NativeStamp { get; set; } = "";
    public PregnancyWorld World { get; set; }
}

// Created only by an actual native Save call. Capturing has no filesystem effects.
// The destination comes from that call's selected slot, never from the loaded slot.
public sealed class PregnancySaveRequest
{
    public string NativePath { get; }
    private readonly PregnancyWorld _snapshot;
    public PregnancySaveRequest(string nativePath, PregnancyWorld world)
    {
        NativePath = Path.GetFullPath(nativePath);
        _snapshot = world.Clone();
    }
    public void Commit(string nativeHash, string nativeStamp) => PregnancyStorage.Commit(NativePath, nativeHash, nativeStamp, _snapshot);
}

// Save() may replace the requested name (especially for native autosaves). Freeze
// state immediately, but bind its destination only after the game's chooser runs.
public class PregnancySaveCapture
{
    public string RequestedName { get; }
    public bool IsAuto { get; }
    public PregnancySaveRequest Request { get; private set; }
    private readonly PregnancyWorld _snapshot;
    public PregnancySaveCapture(string requestedName, bool isAuto, PregnancyWorld world)
    {
        RequestedName = requestedName;
        IsAuto = isAuto;
        _snapshot = world.Clone();
    }
    public bool Matches(string name, bool isAuto) => Request == null && IsAuto == isAuto &&
        string.Equals(RequestedName, name, StringComparison.Ordinal);
    public void Resolve(string actualPath)
    {
        if (Request != null) throw new InvalidOperationException("Save destination already resolved.");
        Request = new PregnancySaveRequest(actualPath, _snapshot);
    }
}

// Companion files never alter the game's .sav. A hash ties each state to exactly one native file.
public static class PregnancyStorage
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static string Sidecar(string nativePath) => nativePath + ".alpregnancy.json";
    public static string Hash(string path)
    {
        if (!File.Exists(path)) return "";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }
    private static void AtomicWrite<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, JsonOptions);
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
        else File.Move(temp, path);
    }
    public static void Commit(string path, string hash, string stamp, PregnancyWorld world)
    {
        if (string.IsNullOrEmpty(hash)) throw new InvalidDataException("Native save is missing.");
        world.Validate();
        AtomicWrite(Sidecar(path), new PregnancySaveEnvelope { NativeHash = hash, NativeStamp = stamp, World = world });
    }
    public static PregnancyWorld Load(string path, string nativeStamp, out string status, PregnancyRules migrationRules = null, int startDay = 40)
    {
        string hash = Hash(path);
        bool hadData = false;
        // Loading is read-only. Old 0.2.0 global cycle registries / pending journals
        // are intentionally not used: only an explicitly saved slot restores state.
        foreach (string candidate in new[] { Sidecar(path), Sidecar(path) + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            hadData = true;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(candidate));
                var root = document.RootElement;
                int version = root.GetProperty("Version").GetInt32();
                if ((version < 1 || version > 3) || root.GetProperty("NativeHash").GetString() != hash || hash.Length == 0) continue;
                var world = PregnancyMigration.Read(root.GetProperty("World"), migrationRules ?? new PregnancyRules(), startDay);
                status = candidate.EndsWith(".bak") ? "Loaded matching backup" : "Loaded pregnancy save";
                return world;
            }
            catch (Exception) { /* Try backup; never overwrite an unrecognized state with empty data. */ }
        }
        if (hadData) throw new InvalidDataException("Pregnancy save does not match this game save, or is damaged. Files kept; gameplay paused.");
        status = "Existing game save: pregnancy initialized";
        return new PregnancyWorld();
    }
}
