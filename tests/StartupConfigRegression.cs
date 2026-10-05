using ALPregnancy;
using BepInEx.Configuration;

internal static class StartupConfigRegression
{
    internal static void Run(Action<string, bool> check)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "startup-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "startup.cfg");
        const string text = "[Pregnancy]\nConception mode = ComplexCycle\n[Pregnancy progression]\nGestation days = 4\nRecovery days = 26\n[User preserved]\nCustom = unchanged\n";
        File.WriteAllText(path, text);
        var old = new ConfigFile(path, false);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool reproduced = false;
            try { PregnancyConfig.Bind(old); }
            catch (IOException) { reproduced = true; }
            check("Actual BepInEx Bind reproduces startup failure with a Windows write lock", reproduced);
            check("Failed old binding has not overwritten existing settings", File.ReadAllText(path) == text);
        }

        var config = new ConfigFile(path, false);
        var warnings = new List<string>();
        using var writer = new StartupConfigWrite(config, warnings.Add);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            PregnancyConfig.Bind(config);
            writer.CompleteBinding();
            check("Actual pregnancy binding completes while configuration is write-locked", config.Count == 20);
            check("Startup reads configured values instead of defaults", PregnancyConfig.Mode.Value == ConceptionMode.ComplexCycle && PregnancyConfig.Gestation.Value == 4 && PregnancyConfig.Recovery.Value == 26);
            writer.Tick(0);
            check("First failed flush retains pending work without throwing", writer.Pending && warnings.Count == 1 && !config.SaveOnConfigSet);
            int notifications = 0;
            void Changed() => notifications++;
            PregnancyConfig.Changed += Changed;
            PregnancyConfig.Gestation.Value = 5;
            PregnancyConfig.Changed -= Changed;
            check("Settings edited during the lock still apply and notify immediately", PregnancyConfig.Rules.GestationDays == 5 && notifications == 1);
            writer.Tick(1); writer.Tick(5); writer.Tick(10);
            check("Persistent lock neither throws nor spams warnings", writer.Pending && warnings.Count == 1);
            check("Pending writes do not modify the locked file", File.ReadAllText(path) == text);
        }
        writer.Tick(11);
        check("Write retries are throttled after the lock is released", writer.Pending && File.ReadAllText(path) == text);
        writer.Tick(15);
        check("Unlock completes startup saving and restores ordinary autosave", !writer.Pending && config.SaveOnConfigSet);
        var saved = File.ReadAllText(path);
        check("Retry saves the latest live value and preserves unknown entries", saved.Contains("Gestation days = 5") && saved.Contains("Custom = unchanged"));
        PregnancyConfig.Recovery.Value = 27;
        check("F1-style settings autosave normally after recovery", File.ReadAllText(path).Contains("Recovery days = 27"));
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            writer.Tick(100);
            check("Successful startup save is not repeated every frame", !writer.Pending && warnings.Count == 1);
        }

        var disabledPath = Path.Combine(directory, "autosave-off.cfg");
        var disabled = new ConfigFile(disabledPath, false) { SaveOnConfigSet = false };
        using (var disabledWriter = new StartupConfigWrite(disabled, warnings.Add))
        {
            disabled.Bind("Test", "Value", 1); disabledWriter.CompleteBinding(); disabledWriter.Tick(0);
            check("Existing autosave-disabled configuration remains unwritten", !disabledWriter.Pending && !File.Exists(disabledPath));
        }
        check("Writer preserves the original autosave preference", !disabled.SaveOnConfigSet);
        var abortedPath = Path.Combine(directory, "aborted.cfg");
        var aborted = new ConfigFile(abortedPath, false);
        using (var abortedWriter = new StartupConfigWrite(aborted, warnings.Add))
            aborted.Bind("Partial", "Value", 1);
        check("Aborted binding restores autosave without flushing partial entries", aborted.SaveOnConfigSet && !File.Exists(abortedPath));
    }
}

