using AL.H;
using AL.User;
using BepInEx;
using Character;
using HarmonyLib;
using UnityEngine;
using Game = Manager.Game;

namespace ALPregnancy;

internal static class PregnancyRuntime
{
    private static readonly System.Random Random = new();
    private static readonly DayBoundary Calendar = new();
    private static readonly Dictionary<IntPtr, Visual> Visuals = new();
    private static readonly List<PendingWrite> Writes = new();
    private static readonly object WriteLock = new();
    private static readonly HashSet<IntPtr> NewWorlds = new();
    private static readonly HashSet<string> Events = new();
    private static readonly Dictionary<IntPtr, (SaveData Data, PregnancyWorld World)> SaveCopies = new();
    private static IntPtr _savePointer;
    private static IntPtr _counterPointer;
    private static string _loadRequest;
    private static string _slot;
    private static float _nextPoll, _nextScan;
    private static bool _blocked, _hooksReady;
    private static bool _settingsChanged;
    private static string FemaleDirectory => Path.Combine(Paths.GameRootPath, "UserData", "chara", "female");
    internal static PregnancyWorld World { get; private set; }
    internal static string Status { get; private set; } = "Waiting for a game save";
    internal static string LastEvent { get; private set; } = "No qualifying event yet";
    internal static bool Active => _hooksReady && !_blocked && World != null;
    private sealed class Visual { public Human Human; public int Id; public MeshLease Lease; }
    private sealed class PendingWrite : PregnancySaveCapture
    {
        public PendingWrite(string name, bool isAuto, PregnancyWorld world) : base(name, isAuto, world) { }
        public string Path => Request?.NativePath;
        public SaveData Data;
        public string PreviousStamp;
        public string ExpectedStamp;
        public DateTime Started = DateTime.UtcNow;
        public DateTime PreviousWrite;
        public DateTime SeenWrite;
        public long SeenLength;
        public DateTime StableSince;
    }

    internal static void Install(Harmony harmony)
    {
        void Hook(Type type, string method, string before = null, string after = null)
        {
            var original = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.FullName, method);
            harmony.Patch(original, before == null ? null : new HarmonyMethod(typeof(PregnancyRuntime), before), after == null ? null : new HarmonyMethod(typeof(PregnancyRuntime), after));
        }
        try
        {
            Hook(typeof(SaveData), nameof(SaveData.Save), nameof(BeforeSave), nameof(AfterSave));
            // The native Save state machine calls this once before ConvertFilePath.
            // Observe the result; calling the chooser ourselves could delete an auto slot.
            Hook(typeof(SaveData), nameof(SaveData.Method_Internal_Static_String_Boolean_String_0), after: nameof(AfterChooseSaveName));
            Hook(typeof(SaveData), nameof(SaveData.UpdateSaveTime), after: nameof(AfterSaveTime));
            Hook(typeof(SaveData), nameof(SaveData.Load), nameof(BeforeLoad));
            Hook(typeof(SaveData), nameof(SaveData.Initialize), after: nameof(AfterInitialize));
            Hook(typeof(SaveData), nameof(SaveData.Copy), after: nameof(AfterCopy));
            Hook(typeof(Game), "set_SaveData", after: nameof(AfterSetSaveData));
            Hook(typeof(Cycle), nameof(Cycle.IncrementTimeZone), nameof(BeforeTime), nameof(AfterTime));
            Hook(typeof(Counter), nameof(Counter.Add), after: nameof(AfterCounter));
            _hooksReady = true;
            Log("Gameplay hooks ready: save/load, calendar, event counter. Three conception modes.");
        }
        catch (Exception ex)
        {
            _blocked = true;
            Status = "Gameplay hooks failed; see log";
            PregnancyPlugin.Logger.LogError(ex);
        }
    }
    private static void Log(string text) => PregnancyPlugin.Logger.LogInfo("[Pregnancy] " + text);
    private static void Report(string text) { LastEvent = text; Log(text); }
    private static SaveData Current()
    {
        // Instance is a non-creating getter in this game's SingletonInitializer.
        var game = Game.Instance;
        return game == null ? null : game.SaveData;
    }
    private static bool Same(SaveData data) => data != null && data.Pointer == _savePointer;
    internal static void OnSettingsChanged()
    {
        _settingsChanged = true;
        _nextPoll = 0;
    }
    internal static bool ApplyDebug(PregnancyWorld expected, string key, PregnancyDebugCommand command, double value = 0)
    {
        try
        {
            var data = Current();
            if (!Active || !Same(data) || !data.Ready) return false;
            if (!PregnancyDebugActions.Apply(World, expected, key, command, value, PregnancyConfig.Rules)) return false;
            if (PregnancyController.ManualPreviewActive) PregnancyController.StopForWorldChange();
            _nextPoll = 0; // Apply the new rate / release meshes in the next normal update.
            var p = World.Characters[key];
            Report($"Debug {command}: {p.Name}, pregnant={p.IsPregnant}, progress={p.PregnancyProgress:F6}, recovery={p.RecoveryProgress:F6}. Memory only; save in game to keep.");
            return true;
        }
        catch (Exception ex)
        {
            Log("Debug edit failed: " + ex.Message);
            return false;
        }
    }
    internal static void Tick()
    {
        if (!_hooksReady || Time.unscaledTime < _nextPoll) return;
        _nextPoll = Time.unscaledTime + .25f;
        try
        {
            if (_settingsChanged)
            {
                _settingsChanged = false;
                Log("F1 settings applied live: " + PregnancyConfig.LastChange + "; existing progress retained.");
            }
            FinishWrites();
            var data = Current();
            if (data == null || !data.Ready)
            {
                // A transient save/loading flag is not a character mesh teardown.
                // Save replacement and Human.Dispose already release their owners.
                return;
            }
            if (data.Pointer != _savePointer) Activate(data);
            if (!Active) return;
            PatchCompatibility.Refresh();
            ObserveTime(data);
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 10;
                ScanCards();
                RegisterRoster(data);
            }

        }
        catch (Exception ex)
        {
            ReleaseVisuals();
            Status = "Runtime error: " + ex.Message;
            PregnancyPlugin.Logger.LogError("[Pregnancy] " + ex);
            _nextPoll = Time.unscaledTime + 5;
        }
    }
    private static string NativePath(string name, bool isAuto = false)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string path = Path.IsPathRooted(name) ? Path.GetFullPath(name) : Path.GetFullPath(SaveData.ConvertFilePath(name, isAuto, false));
        string root = Path.GetFullPath(Path.Combine(Paths.GameRootPath, "UserData", "save")) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Save path outside game's UserData/save: " + path);
        return path;
    }
    private static void Activate(SaveData data, bool forceNew = false)
    {
        if (data == null || !data.Ready) return;
        FinishWrites(true);
        PregnancyController.StopForWorldChange();
        ReleaseVisuals();
        _savePointer = data.Pointer;
        _blocked = false;
        Events.Clear();
        SaveCopies.Clear();
        _counterPointer = IntPtr.Zero;
        try
        {
            bool initialized;
            lock (WriteLock) initialized = NewWorlds.Remove(data.Pointer);
            // FileName belongs to the installed SaveData. A stale/cancelled load request must
            // not override it or turn a newly initialized game into the previous save slot.
            bool fresh = forceNew || (initialized && string.IsNullOrWhiteSpace(data.FileName));
            _slot = fresh ? null : NativePath(string.IsNullOrWhiteSpace(data.FileName) ? _loadRequest : data.FileName);
            _loadRequest = null;
            World = !fresh && _slot != null && File.Exists(_slot)
                ? PregnancyStorage.Load(_slot, data.SaveTimeText, out var loadedStatus, PregnancyConfig.Rules, BellyDeformSettings.StartDay)
                : new PregnancyWorld();
            Status = _slot == null ? "New game (not yet saved)" : "Active slot: " + Path.GetFileName(_slot);
            Calendar.Reset(data.Cycle?.TimeZone ?? -1);
            ScanCards();
            RegisterRoster(data);
            _nextScan = Time.unscaledTime + 10;
            Log(Status + $"; day={World.ElapsedDays}, records={World.Characters.Count}, cards={World.CardCycleProgress.Count}");
        }
        catch (Exception ex)
        {
            _blocked = true;
            World = null;
            Status = ex.Message;
            PregnancyPlugin.Logger.LogError("[Pregnancy] Load paused: " + ex);
        }
    }
    private static string CardKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string normalized = name.Replace('\\', '/');
        string marker = "/chara/female/";
        int at = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at >= 0) normalized = normalized[(at + marker.Length)..];
        else if (Path.IsPathRooted(name)) normalized = Path.GetRelativePath(FemaleDirectory, name).Replace('\\', '/');
        normalized = normalized.TrimStart('/');
        if (normalized.StartsWith("female/", StringComparison.OrdinalIgnoreCase)) normalized = normalized[7..];
        if (!normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) normalized += ".png";
        return normalized.ToLowerInvariant();
    }
    private static void ScanCards()
    {
        if (World == null || !Directory.Exists(FemaleDirectory)) return;
        foreach (string path in Directory.EnumerateFiles(FemaleDirectory, "*.png", SearchOption.AllDirectories))
        {
            string key = CardKey(path);
            World.EnsureCardCycle(key, Random);
        }
    }
    private static string ActorKey(ActorData data) => "npc:" + data.UniqueID + ":" + (data.UserID ?? "") + ":" + (data.DataID ?? "") + ":" + CardKey(data.CharaFileName);
    private static string ActorName(ActorData data) => data.HumanData?.Parameter?.fullname ?? Path.GetFileNameWithoutExtension(data.CharaFileName) ?? "Character";
    private static PregnancyRecord Register(NPCData data)
    {
        if (data == null || data.HumanData?.Parameter?.sex != 1) return null;
        string card = CardKey(data.CharaFileName);
        int day = World.EnsureCardCycle(card, Random);
        return World.Register(ActorKey(data), ActorName(data), day);
    }
    private static void RegisterRoster(SaveData data)
    {
        var list = data.NPCDataList;
        if (list == null) return;
        for (int i = 0; i < list.Count; i++) Register(list[i]);
    }
    private static void ObserveTime(SaveData data)
    {
        if (data?.Cycle == null || !Same(data)) return;
        if (Calendar.Observe(data.Cycle.TimeZone) && Active && PregnancyPlugin.Enabled.Value && PregnancyConfig.Gameplay.Value)
        {
            ScanCards();
            RegisterRoster(data);
            PregnancySimulation.NextDay(World, PregnancyConfig.Rules, Random, Report);
            Log($"New day {World.ElapsedDays}: cycles +1; pregnant={World.Characters.Values.Count(x => x.IsPregnant)}.");
        }
    }
    private static void BeforeTime(Cycle __instance)
    {
        try { var data = Current(); if (Active && Same(data) && data.Cycle?.Pointer == __instance.Pointer) ObserveTime(data); }
        catch (Exception ex) { Log("Day prefix: " + ex.Message); }
    }
    private static void AfterTime(Cycle __instance)
    {
        try { var data = Current(); if (Active && Same(data) && data.Cycle?.Pointer == __instance.Pointer) ObserveTime(data); }
        catch (Exception ex) { Log("Day postfix: " + ex.Message); }
    }
    private static void AfterCounter(Counter __instance, int category, Counter.Target target, Counter.Kind kind)
    {
        try
        {
            if (!Active || !PregnancyPlugin.Enabled.Value || !PregnancyConfig.Gameplay.Value) return;
            if ((kind & Counter.Kind.Inside) == 0) return;
            var scene = HScene.Instance;
            if (scene == null || scene.Counter?.Pointer != __instance.Pointer || scene.Controller == null) return;
            if (_counterPointer != __instance.Pointer) { Events.Clear(); _counterPointer = __instance.Pointer; }
            string eventKey = __instance._history.Count + ":" + category;
            if (!Events.Add(eventKey)) return;
            var controller = scene.Controller;
            var insertion = controller.GetInsetType();
            Log($"Inside event: category={category}, kind={kind}, target={target}, insertion={insertion}.");
            if ((target & Counter.Target.Player) == 0) return;
            if ((kind & Counter.Kind.Anal) != 0 || insertion != PostureSubSelecter.ChangeData.Insert.Vagina) return;
            var actors = controller._actors;
            if (actors == null) return;
            // ActorType expresses motion roles; female-led positions can reverse those roles.
            // A single male plus a single female remains unambiguous in either motion category.
            var fathers = actors.Where(x => x != null && x.Active && x.IsMan).ToArray();
            if (fathers.Length == 0) fathers = actors.Where(x => x != null && x.Active && x.IsPC && x.IsFutanari).ToArray();
            var receivers = actors.Where(x => x != null && x.Active && x.Sex == 1 && !fathers.Any(f => f.Pointer == x.Pointer)).ToArray();
            // Some 3P motions have different insertion types per participant. Never assign both by guessing.
            if (fathers.Length != 1 || receivers.Length != 1) { Report("Ambiguous multi-character event skipped; see diagnostic log."); return; }
            var receiver = receivers[0];
            if (receiver.Actor == null) return; // Free scenes supply Human objects without live game actors.
            var npc = receiver.NPCData;
            var data = Current();
            if (!Same(data) || npc == null) return;
            var savedNpc = data.GetNPCDataUniqueID(npc.UniqueID);
            if (savedNpc == null || ActorKey(savedNpc) != ActorKey(npc)) return;
            var p = Register(savedNpc);
            if (p == null) return;
            var father = fathers[0];
            var rules = PregnancyConfig.Rules;
            var chance = PregnancyChance.Evaluate(p, rules);
            Log($"Conception check: {p.Name}, mode={rules.Mode}, cycle={p.CycleDay}, base={chance.BaseChance:P2}, residual={chance.ResidualFactor:F3}, egg={chance.EggFactor:F2}, effectiveBeforeEvent={chance.EffectiveChance:P2}, reason={chance.Reason}, eligible={p.CanConceive(rules)}, history={eventKey}.");
            bool conceived = PregnancySimulation.Exposure(p, rules,
                father.Actor?.BaseData == null ? father.Data?.About?.dataID ?? father.Name : ActorKey(father.Actor.BaseData), father.Name, Random);
            Report(p.Name + (conceived ? ": conceived; father " + p.FatherName : p.CanConceive(rules) && rules.Mode == ConceptionMode.ComplexCycle ? ": residual set to 1; daily check pending" : ": conception event checked (no new pregnancy)"));
        }
        catch (Exception ex) { PregnancyPlugin.Logger.LogError("[Pregnancy] Event: " + ex); }
    }
    private static void BeforeLoad(string fileName)
    {
        try { FinishWrites(true); _loadRequest = fileName; }
        catch (Exception ex) { Log("Before load: " + ex.Message); }
    }
    private static void AfterSetSaveData(Game __instance)
    {
        try { var data = __instance.SaveData; if (data != null && data.Ready && (!Same(data) || _loadRequest != null)) Activate(data); }
        catch (Exception ex) { Log("Set save: " + ex.Message); }
    }
    private static void AfterInitialize(SaveData __instance)
    {
        lock (WriteLock) NewWorlds.Add(__instance.Pointer);
        if (Same(__instance)) { _loadRequest = null; _savePointer = IntPtr.Zero; ReleaseVisuals(); }
    }
    private static void AfterCopy(SaveData __instance, SaveData source)
    {
        try
        {
            if (__instance.Pointer == source.Pointer) return;
            if (Same(__instance)) { _loadRequest = source.FileName; Activate(__instance); }
            else if (Active && Same(source))
            {
                // Some save UIs serialize a copy. Keep its world frozen at exactly the same moment.
                if (SaveCopies.Count >= 32) SaveCopies.Clear();
                SaveCopies[__instance.Pointer] = (__instance, World.Clone());
            }
        }
        catch (Exception ex) { Log("Copy save: " + ex.Message); }
    }
    private static void BeforeSave(SaveData __instance, string fileName, bool isAuto)
    {
        try
        {
            if (!Active) return;
            PregnancyWorld snapshot;
            if (Same(__instance))
            {
                ObserveTime(__instance);
                RegisterRoster(__instance);
                snapshot = World.Clone();
            }
            else if (SaveCopies.Remove(__instance.Pointer, out var copy)) snapshot = copy.World;
            else return;
            lock (WriteLock)
            {
                FinishWrites(true);
                var pending = new PendingWrite(fileName, isAuto, snapshot)
                {
                    Data = __instance, PreviousStamp = __instance.SaveTimeText ?? "", ExpectedStamp = ""
                };
                Writes.Add(pending);
            }
            Status = "Game save requested; waiting for its actual slot";
        }
        catch (Exception ex) { PregnancyPlugin.Logger.LogError("[Pregnancy] Snapshot failed: " + ex); }
    }
    private static void AfterChooseSaveName(bool isAuto, string fileName, string __result)
    {
        try
        {
            lock (WriteLock)
            {
                var matches = Writes.Where(x => x.Matches(fileName, isAuto)).ToArray();
                if (matches.Length != 1)
                {
                    foreach (var match in matches) Writes.Remove(match);
                    if (matches.Length > 1) Log("Ambiguous overlapping save requests; companions not written.");
                    return;
                }
                var pending = matches[0];
                // This converter is pure with checksDirectory=false; it receives exactly
                // the name and auto flag that the native Save uses after this callback.
                string path = NativePath(__result, isAuto);
                if (path == null) { Writes.Remove(pending); return; }
                var overlapping = Writes.Where(x => x != pending && string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (overlapping.Length != 0)
                {
                    foreach (var other in overlapping) Writes.Remove(other);
                    Writes.Remove(pending);
                    Log("Overlapping native writes to " + path + "; companions not written.");
                    return;
                }
                pending.Resolve(path);
                pending.PreviousWrite = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                pending.ExpectedStamp = pending.Data.SaveTimeText ?? "";
                Log($"Native save target resolved: requested={fileName}, auto={isAuto}, actual={path}");
            }
        }
        catch (Exception ex) { Log("Native save target: " + ex.Message); }
    }
    private static void AfterSave(SaveData __instance) => AfterSaveTime(__instance);
    private static void AfterSaveTime(SaveData __instance)
    {
        try
        {
            lock (WriteLock)
                foreach (var p in Writes.Where(x => x.Data.Pointer == __instance.Pointer))
                {
                    string stamp = __instance.SaveTimeText ?? "";
                    if (p.ExpectedStamp == stamp || (!string.IsNullOrEmpty(p.ExpectedStamp) && p.ExpectedStamp != p.PreviousStamp)) continue;
                    p.ExpectedStamp = stamp;
                }
        }
        catch (Exception ex) { Log("Save timestamp: " + ex.Message); }
    }
    private static void FinishWrites(bool flushClosed = false)
    {
        lock (WriteLock)
        foreach (var p in Writes.ToArray())
        {
            try
            {
                AfterSaveTime(p.Data);
                if (p.Request == null) continue;
                if (!File.Exists(p.Path)) continue;
                var info = new FileInfo(p.Path);
                if (info.LastWriteTimeUtc == p.PreviousWrite) continue;
                if (p.SeenWrite != info.LastWriteTimeUtc || p.SeenLength != info.Length)
                {
                    p.SeenWrite = info.LastWriteTimeUtc; p.SeenLength = info.Length; p.StableSince = DateTime.UtcNow;
                    if (!flushClosed) continue;
                }
                if (!flushClosed && (DateTime.UtcNow - p.StableSince).TotalSeconds < 1) continue;
                // Hash opens with FileShare.Read: an active writer cannot coexist with this handle.
                string hash = PregnancyStorage.Hash(p.Path);
                p.Request.Commit(hash, p.ExpectedStamp);
                Writes.Remove(p);
                if (Same(p.Data)) _slot = p.Path;
                Status = "Pregnancy saved: " + Path.GetFileName(p.Path);
                Log(Status + "; SHA256=" + hash);
            }
            catch (IOException) { /* Native asynchronous writer still owns the file; retry on next tick. */ }
            catch (Exception ex) { Log("Companion save: " + ex.Message); }
            finally
            {
                if ((DateTime.UtcNow - p.Started).TotalSeconds > 120 && Writes.Remove(p))
                    Log("Native save did not complete in 120 seconds; previous companion retained; unsaved state stays in memory: " + (p.Path ?? p.RequestedName));
            }
        }
    }
    internal static void UpdateHumanVisual(Human human)
    {
        if (human == null) return;
        if (!PregnancyPlugin.Enabled.Value || PregnancyController.ManualPreviewActive || !human.HiPoly || human.Sex != 1)
        { Release(human.Pointer); return; }
        var data = Current();
        if (data == null || !data.Ready) return;
        if (!Same(data)) Activate(data);
        if (!Active) return;
        PregnancyRecord record = null;
        var game = Game.Instance;
        if (game?.Heroines != null)
            for (int i = 0; i < game._heroines.Count; i++)
            {
                var heroine = game.Heroines[i];
                if (heroine?.BaseData != null && heroine.Human?.Pointer == human.Pointer)
                { World.Characters.TryGetValue(ActorKey(heroine.BaseData), out record); break; }
            }
        var scene = HScene.Instance;
        if (record == null && scene?.Actors != null)
            foreach (var actor in scene.Actors)
                if (actor?.NPCData != null && actor.Human?.Pointer == human.Pointer)
                { World.Characters.TryGetValue(ActorKey(actor.NPCData), out record); break; }
        if (record == null) return; // The native actor owner may still be assigning this Human.
        float growth = record.Growth(PregnancyConfig.Rules);
        if (growth <= 0) { Release(human.Pointer); return; }
        if (!MorphReadiness.TryBeginApply(human)) return;
        Visuals.TryGetValue(human.Pointer, out var visual);
        var meshes = MorphReadiness.CurrentMeshes(human);
        var changes = visual?.Lease.CheckChanges(meshes) ?? default;
        if (visual != null && changes.Mesh)
        { Release(human.Pointer); visual = null; }
        if (visual == null)
        {
            visual = new Visual { Human = human, Id = human.GameObject.GetInstanceID(), Lease = new MeshLease() };
            visual.Lease.Acquire(human, meshes);
            Visuals[human.Pointer] = visual;
            Log($"Native completion: acquired actor={visual.Id}, readable meshes={visual.Lease.ReadableCount}; applying before returning to native caller.");
        }
        if (changes.Visibility) BellyVertexMorph.Invalidate(visual.Id);
        BellyVertexMorph.Apply(human, visual.Id, growth);
    }
    private static void Release(IntPtr pointer)
    {
        if (!Visuals.Remove(pointer, out var visual)) return;
        try { BellyVertexMorph.Forget(visual.Id); }
        finally { visual.Lease.Dispose(); }
    }
    internal static void ReleaseHuman(Human human) { if (human != null) Release(human.Pointer); }
    internal static void ReleaseVisuals() { foreach (var pointer in Visuals.Keys.ToArray()) Release(pointer); }
    internal static void Shutdown() { FinishWrites(true); ReleaseVisuals(); }
}
