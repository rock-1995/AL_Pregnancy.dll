using ALPregnancy;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length > 0 && args[0] == "--migrate")
{
    if (args.Length != 4) throw new ArgumentException("--migrate <backup save path> <backup config path> <output directory>");
    MigrateBackup(args[1], args[2], args[3]);
    return 0;
}

var tests = new (string Name, Action Run)[]
{
    ("Slider endpoints, clamping and invalid values", SliderValues),
    ("Cloned appearance and worlds have independent mutable state", IndependentClones),
    ("New character records start at zero and validate as v4", NewRecords),
    ("v3 migration preserves all existing fields and computes visual amounts", V3Migration),
    ("v1 and v2 continue through existing normalized-state migration", OlderVersions),
    ("v4 loads preserve authored slider values across configuration changes", V4Values),
    ("Invalid or missing v4 appearance is rejected before clamping", InvalidV4),
    ("Unknown future save versions are rejected", UnknownVersions),
    ("Load is read-only; explicit commit writes v4 and leaves native save intact", StorageBoundary),
};

int failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static T Expect<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T exception) { return exception; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

static JsonNode Node(object value) => JsonNode.Parse(JsonSerializer.Serialize(value));
static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);
static PregnancyWorld Read(JsonNode node, PregnancyRules rules = null, int bodyBirths = 5) =>
    PregnancyMigration.Read(Element(node), rules ?? new PregnancyRules(), 40, bodyBirths);

static PregnancyWorld SampleWorld()
{
    var world = new PregnancyWorld { WorldId = "migration-test-world", ElapsedDays = 456 };
    var pregnant = world.Register("pregnant", "Test character", 13);
    pregnant.Pregnant = true;
    pregnant.PregnancyProgress = .57;
    pregnant.Births = 2;
    pregnant.FatherKey = "father-key";
    pregnant.FatherName = "Father";
    pregnant.LastFatherName = "Previous father";
    pregnant.LastBirthDay = 322;
    pregnant.GestationVariationSample = .734;
    pregnant.GrowthStartScale = 1.4;
    var postpartum = world.Register("postpartum", "Postpartum", 3);
    postpartum.Births = 1;
    postpartum.RecoveryProgress = .75;
    postpartum.Residual = .25;
    postpartum.ResidualFatherKey = "another-father-key";
    postpartum.ResidualFatherName = "Another father";
    world.Register("untouched", "Untouched", 28);
    world.CardCycleProgress["external-card"] = CyclePhase.Progress(7);
    return world;
}

static JsonNode LegacyV3(PregnancyWorld world)
{
    var node = Node(world);
    node["Version"] = 3;
    foreach (var entry in node["Characters"].AsObject()) entry.Value.AsObject().Remove("Appearance");
    return node;
}

static void SliderValues()
{
    var values = new AppearanceValues();
    Check(values.Belly == 0 && values.Body == 0, "Nonzero defaults.");
    values.Belly = -1; values.SetBody(2);
    Check(values.Belly == 0 && values.Body == 1, "Bounds were not clamped.");
    values.SetBelly(.375f); values.Body = .8125f;
    foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
    {
        Expect<ArgumentOutOfRangeException>(() => values.Belly = invalid);
        Expect<ArgumentOutOfRangeException>(() => values.SetBody(invalid));
        Check(values.Belly == .375f && values.Body == .8125f, "Rejected value changed prior state.");
    }
}

static void IndependentClones()
{
    var values = new AppearanceValues { Belly = .3f, Body = .8f };
    var cloned = values.Clone();
    cloned.Belly = .6f;
    Check(values.Belly == .3f && cloned.Body == .8f, "Appearance clone shares state or loses values.");
    var world = SampleWorld();
    world.Characters["pregnant"].Appearance = values;
    var worldCopy = world.Clone();
    worldCopy.Characters["pregnant"].Appearance.Body = .9f;
    Check(world.Characters["pregnant"].Appearance.Body == .8f, "World clone shares appearance state.");
}

static void NewRecords()
{
    var world = new PregnancyWorld();
    var record = world.Register("new", "New", 4);
    Check(world.Version == 4 && record.Appearance.Belly == 0 && record.Appearance.Body == 0, "New record defaults failed.");
    world.Validate();
    record.Appearance = null;
    Expect<InvalidDataException>(world.Validate);
}

static void V3Migration()
{
    var legacy = LegacyV3(SampleWorld());
    var rules = new PregnancyRules { GrowthStart = .1, GrowthEarlyEnd = .25, GrowthMiddleEnd = .5, GrowthLateStart = .8 };
    foreach (int births in new[] { 5, 4, 1, 0, -1 })
    {
        var migrated = Read(legacy, rules, births);
        Check(migrated.Version == 4, "World was not upgraded.");
        CheckOldFields(legacy, Node(migrated), "World");
        foreach (var record in migrated.Characters.Values)
        {
            Check(record.Appearance.Belly == record.Growth(rules), "Belly does not match existing growth.");
            Check(record.Appearance.Body == (float)BodyChangeMath.Progress(record.Pregnant, record.PregnancyProgress, record.Births, births), "Body does not match existing progression.");
        }
    }
    Check(legacy["Version"].GetValue<int>() == 3 && !legacy["Characters"]["pregnant"].AsObject().ContainsKey("Appearance"), "Migration mutated source JSON.");
}

static void OlderVersions()
{
    foreach (int version in new[] { 1, 2 })
    {
        var legacy = LegacyV3(SampleWorld());
        legacy["Version"] = version;
        foreach (var entry in legacy["Characters"].AsObject())
        {
            var record = entry.Value.AsObject();
            bool pregnant = record["Pregnant"].GetValue<bool>();
            record["CycleDay"] = 9;
            record.Remove("CycleProgress");
            record.Remove("GestationVariationSample");
            if (version == 2) record["GestationVariationRoll"] = .4;
            else
            {
                record["PregnantDays"] = pregnant ? 100 : -1;
                record["DueDays"] = 200;
                record["RecoveryDays"] = pregnant ? 0 : 2;
                foreach (string name in new[] { "Pregnant", "PregnancyProgress", "RecoveryProgress", "GrowthStartScale" }) record.Remove(name);
            }
        }
        legacy.AsObject().Remove("CardCycleProgress");
        legacy["CardCycles"] = new JsonObject { ["external-card"] = 7 };
        var rules = new PregnancyRules { RecoveryDays = 4 };
        var upgraded = Read(legacy, rules, 4);
        var pregnantRecord = upgraded.Characters["pregnant"];
        Check(upgraded.Version == 4 && pregnantRecord.CycleDay == 9, "Legacy cycle migration failed.");
        Check(pregnantRecord.PregnancyProgress == (version == 1 ? .5 : .57), "Legacy pregnancy migration failed.");
        Check(pregnantRecord.GestationVariationSample == (version == 1 ? .5 : .7), "Legacy variation migration failed.");
        Check(pregnantRecord.Appearance.Belly == pregnantRecord.Growth(rules), "Legacy appearance not initialized.");
        Check(pregnantRecord.FatherKey == "father-key" && pregnantRecord.LastBirthDay == 322 && pregnantRecord.Births == 2, "Legacy history changed.");
        Check(upgraded.CardCycleProgress["external-card"] == CyclePhase.Progress(7), "Legacy card cycles changed.");
    }
}

static void V4Values()
{
    var world = SampleWorld();
    world.Characters["pregnant"].Appearance = new AppearanceValues { Belly = .91f, Body = .08f };
    var loaded = Read(Node(world), new PregnancyRules { GrowthStart = .9 }, 1);
    Check(loaded.Characters["pregnant"].Appearance.Belly == .91f && loaded.Characters["pregnant"].Appearance.Body == .08f, "Authored v4 values were recalculated.");
}

static void InvalidV4()
{
    foreach (string malformed in new[] { "null", "{}", "{\"Belly\":-0.1,\"Body\":0}", "{\"Belly\":0,\"Body\":1.1}", "{\"Belly\":1e400,\"Body\":0}", "{\"Belly\":\"NaN\",\"Body\":0}" })
    {
        var node = Node(SampleWorld());
        node["Characters"]["pregnant"]["Appearance"] = JsonNode.Parse(malformed);
        Expect<InvalidDataException>(() => Read(node));
    }
    var absent = Node(SampleWorld());
    absent["Characters"]["pregnant"].AsObject().Remove("Appearance");
    Expect<InvalidDataException>(() => Read(absent));
}

static void UnknownVersions()
{
    var node = Node(SampleWorld());
    node["Version"] = 5;
    Expect<InvalidDataException>(() => Read(node));
}

static void StorageBoundary()
{
    string directory = Path.Combine(AppContext.BaseDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string nativePath = Path.Combine(directory, "slot.sav");
    byte[] nativeBytes = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
    File.WriteAllBytes(nativePath, nativeBytes);
    string hash = PregnancyStorage.Hash(nativePath);
    const string stamp = "2026/11/22 6:09:28";
    var old = new JsonObject { ["Version"] = 3, ["NativeHash"] = hash, ["NativeStamp"] = stamp, ["World"] = LegacyV3(SampleWorld()) };
    string sidecar = PregnancyStorage.Sidecar(nativePath);
    File.WriteAllText(sidecar, old.ToJsonString());
    byte[] before = File.ReadAllBytes(sidecar);
    DateTime modified = File.GetLastWriteTimeUtc(sidecar);
    var loaded = PregnancyStorage.Load(nativePath, stamp, out _, bodyBirths: 4);
    Check(File.ReadAllBytes(sidecar).SequenceEqual(before) && File.GetLastWriteTimeUtc(sidecar) == modified, "Load wrote migrated data.");
    Check(!File.Exists(sidecar + ".bak"), "Load created a backup.");
    PregnancyStorage.Commit(nativePath, hash, stamp, loaded);
    var saved = JsonNode.Parse(File.ReadAllText(sidecar));
    Check(saved["Version"].GetValue<int>() == 4 && saved["World"]["Version"].GetValue<int>() == 4, "Commit did not write v4.");
    Check(saved["NativeHash"].GetValue<string>() == hash && saved["NativeStamp"].GetValue<string>() == stamp, "Native binding changed.");
    Check(File.ReadAllBytes(nativePath).SequenceEqual(nativeBytes), "Native save changed.");
    Check(File.ReadAllBytes(sidecar + ".bak").SequenceEqual(before), "Commit did not preserve the previous sidecar.");
    var reloaded = PregnancyStorage.Load(nativePath, stamp, out _, bodyBirths: 1);
    Check(reloaded.Characters["pregnant"].Appearance.Body == loaded.Characters["pregnant"].Appearance.Body, "Saved amount was recomputed.");
}

static void CheckOldFields(JsonNode before, JsonNode after, string path)
{
    if (before is JsonObject obj)
    {
        Check(after is JsonObject, "Changed object at " + path);
        foreach (var entry in obj)
        {
            if (path == "World" && entry.Key == "Version") continue;
            Check(after.AsObject().ContainsKey(entry.Key), "Missing old field " + path + "." + entry.Key);
            CheckOldFields(entry.Value, after[entry.Key], path + "." + entry.Key);
        }
        return;
    }
    if (before == null) { Check(after == null, "Changed null at " + path); return; }
    var a = Element(before); var b = Element(after);
    Check(a.ValueKind == b.ValueKind, "Changed value type at " + path);
    bool same = a.ValueKind == JsonValueKind.Number ? a.GetDouble().Equals(b.GetDouble()) :
        a.ValueKind == JsonValueKind.String ? a.GetString() == b.GetString() : a.GetRawText() == b.GetRawText();
    Check(same, "Changed old field " + path + ": " + a.GetRawText() + " -> " + b.GetRawText());
}

static void MigrateBackup(string nativePath, string configPath, string outputDirectory)
{
    nativePath = Path.GetFullPath(nativePath);
    configPath = Path.GetFullPath(configPath);
    outputDirectory = Path.GetFullPath(outputDirectory);
    if (string.Equals(Path.GetDirectoryName(nativePath), outputDirectory, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Use a separate output directory to preserve the backup.");
    var config = new Dictionary<string, string>(StringComparer.Ordinal);
    string section = "";
    foreach (string raw in File.ReadLines(configPath))
    {
        string line = raw.Trim();
        if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1]; continue; }
        if (line.Length == 0 || line.StartsWith('#')) continue;
        int equals = line.IndexOf('=');
        if (equals > 0) config[section + "/" + line[..equals].Trim()] = line[(equals + 1)..].Trim();
    }
    float Setting(string group, string key) => float.Parse(config[group + "/" + key], CultureInfo.InvariantCulture);
    var rules = new PregnancyRules
    {
        GrowthStart = Setting("Pregnancy belly timing", "Growth start percent") / 100d,
        GrowthEarlyEnd = Setting("Pregnancy belly timing", "Stage 1 end percent") / 100d,
        GrowthMiddleEnd = Setting("Pregnancy belly timing", "Stage 2 end percent") / 100d,
        GrowthLateStart = Setting("Pregnancy belly timing", "Stage 3 end percent") / 100d,
        RecoveryDays = (int)Setting("Pregnancy progression", "Recovery days"),
        GestationDays = (int)Setting("Pregnancy progression", "Gestation days")
    };
    int bodyBirths = (int)Setting("Pregnancy body changes", "Births for full change");
    string sidecarPath = PregnancyStorage.Sidecar(nativePath);
    byte[] sidecarBefore = File.ReadAllBytes(sidecarPath);
    string nativeHash = PregnancyStorage.Hash(nativePath);
    var original = JsonNode.Parse(sidecarBefore);
    string stamp = original["NativeStamp"].GetValue<string>();
    Check(original["NativeHash"].GetValue<string>() == nativeHash, "Backup sidecar does not match native save.");
    var world = PregnancyStorage.Load(nativePath, stamp, out string status, rules, bodyBirths: bodyBirths);
    CheckOldFields(original["World"], Node(world), "World");
    Check(File.ReadAllBytes(sidecarPath).SequenceEqual(sidecarBefore), "Backup sidecar was modified.");
    Directory.CreateDirectory(outputDirectory);
    string outputNative = Path.Combine(outputDirectory, Path.GetFileName(nativePath));
    File.Copy(nativePath, outputNative, true);
    PregnancyStorage.Commit(outputNative, nativeHash, stamp, world);
    Check(PregnancyStorage.Hash(outputNative) == nativeHash, "Copied native save hash changed.");
    var result = JsonNode.Parse(File.ReadAllText(PregnancyStorage.Sidecar(outputNative)));
    CheckOldFields(original["World"], result["World"], "World");
    Check(result["NativeHash"].GetValue<string>() == nativeHash && result["NativeStamp"].GetValue<string>() == stamp, "Native binding changed.");
    var report = new
    {
        Source = sidecarPath, Output = PregnancyStorage.Sidecar(outputNative), SourceVersion = original["Version"].GetValue<int>(),
        OutputVersion = 4, NativeHash = nativeHash, NativeStamp = stamp, NativeBytesUnchanged = true,
        AllExistingWorldFieldsUnchanged = true, SourceFilesUnchanged = true,
        CharacterCount = world.Characters.Count, Config = configPath, BodyBirths = bodyBirths,
        rules.GrowthStart, rules.GrowthEarlyEnd, rules.GrowthMiddleEnd, rules.GrowthLateStart,
        Characters = world.Characters.Values.Select(p => new { p.Key, p.Name, p.Appearance }).ToArray()
    };
    File.WriteAllText(Path.Combine(outputDirectory, "migration-verification.json"), JsonSerializer.Serialize(report, PregnancyStorage.JsonOptions));
    Console.WriteLine($"Migrated {world.Characters.Count} characters to v4; all prior fields, native hash and native stamp verified unchanged.");
    Console.WriteLine($"NativeHash={nativeHash}; NativeStamp={stamp}; BodyBirths={bodyBirths}; load={status}");
    Console.WriteLine(PregnancyStorage.Sidecar(outputNative));
}
