using BepInEx.Configuration;

namespace ALPregnancy;

// ConfigFile.Bind can Save both during entry construction and after registration.
// A sharing violation there aborts Plugin.Load with only some entries initialized.
// Bind everything in memory, then save once from Update; retain pending changes
// and retry on an I/O failure without blocking the game's startup thread.
internal sealed class StartupConfigWrite : IDisposable
{
    private readonly ConfigFile _file;
    private readonly bool _autoSave;
    private readonly Action<string> _warning;
    private double _nextAttempt;
    private bool _reported;
    internal bool Pending { get; private set; }

    internal StartupConfigWrite(ConfigFile file, Action<string> warning)
    {
        _file = file;
        _warning = warning;
        _autoSave = file.SaveOnConfigSet;
        file.SaveOnConfigSet = false;
    }

    // Called only after every Bind succeeds. A different initialization failure
    // must not flush a partially registered configuration.
    internal void CompleteBinding() => Pending = _autoSave;

    internal void Tick(double now, bool force = false)
    {
        if (!Pending || (!force && now < _nextAttempt)) return;
        _nextAttempt = now + 5;
        try
        {
            _file.Save();
            Pending = false;
            _file.SaveOnConfigSet = _autoSave;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_reported) return;
            _reported = true;
            _warning?.Invoke("[Config startup] Write deferred; plugin continues with loaded settings. " +
                "Retrying every 5 seconds: " + ex.Message);
        }
    }

    public void Dispose()
    {
        // Do not save on disposal: startup may have failed halfway through Bind.
        // The caller explicitly flushes completed bindings on normal unload.
        _file.SaveOnConfigSet = _autoSave;
        Pending = false;
    }
}
