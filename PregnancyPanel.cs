using System.Globalization;
using UnityEngine;

namespace ALPregnancy;

// SVS layout: independent character list on the left, selected character controls on the right.
internal static class PregnancyPanel
{
    private static PregnancyWorld _world;
    private static string _selectedKey;
    private static Vector2 _charactersScroll, _detailScroll;
    private static float _detailHeight = 600;
    private static string _dayBuffer = "0";
    private static bool _dayDirty;
    private static double _observedProgress = -1;
    private static double _observedDuration;
    private static string _message = "Edits stay in memory until you save to a game slot.";
    private static GUIStyle _wrapped, _characterButton, _selectedButton;

    internal static void Draw(Rect area)
    {
        if (_wrapped == null)
        {
            // AL strips GUIStyle's copy constructor from its IL2CPP build.
            _wrapped = new GUIStyle { wordWrap = true, fontSize = 14 };
            _wrapped.normal.textColor = Color.white;
        }
        _characterButton ??= GUI.skin.button;
        _selectedButton ??= GUI.skin.box;
        var world = PregnancyRuntime.World;
        if (!ReferenceEquals(_world, world))
        {
            _world = world;
            _selectedKey = null;
            _charactersScroll = _detailScroll = Vector2.zero;
            _dayDirty = false;
            _message = "Edits stay in memory until you save to a game slot.";
            GUI.FocusControl("");
        }
        GUI.BeginGroup(area);
        try
        {
            GUI.Label(new Rect(0, 0, area.width, 24), "Character Debug   |   Global settings: F1 > AL Pregnancy");
            GUI.Label(new Rect(0, 25, area.width, 42), PregnancyRuntime.Status, _wrapped);
            GUI.Label(new Rect(0, 58, area.width, 25), $"F1 live #{PregnancyConfig.Revision}: {PregnancyConfig.LastChange}");
            float top = 88, height = Mathf.Max(80, area.height - top);
            float leftWidth = Mathf.Min(180, area.width * .3f);
            var records = world?.Characters.Values.OrderBy(x => x.Name).ThenBy(x => x.Key).ToArray() ?? Array.Empty<PregnancyRecord>();
            if (_selectedKey == null || !records.Any(x => x.Key == _selectedKey)) Select(records.FirstOrDefault());

            GUI.Box(new Rect(0, top, leftWidth, height), "");
            GUI.Label(new Rect(8, top + 5, leftWidth - 16, 24), $"Characters ({records.Length})");
            var listRect = new Rect(4, top + 32, leftWidth - 8, height - 36);
            _charactersScroll = GUI.BeginScrollView(listRect, _charactersScroll,
                new Rect(0, 0, listRect.width - 18, Math.Max(listRect.height, records.Length * 60)));
            try
            {
                for (int i = 0; i < records.Length; i++)
                {
                    var p = records[i];
                    string shortStatus = p.IsPregnant ? $"Pregnant {p.PregnancyProgress:P1}" : p.RecoveryProgress < 1 ? $"Recovery {p.RecoveryProgress:P1}" : "Not pregnant";
                    bool selected = p.Key == _selectedKey;
                    if (GUI.Button(new Rect(0, i * 60, listRect.width - 20, 55),
                        (selected ? "> " : "") + p.Name + "\n" + shortStatus, selected ? _selectedButton : _characterButton)) Select(p);
                }
            }
            finally { GUI.EndScrollView(); }

            var detail = new Rect(leftWidth + 10, top, area.width - leftWidth - 10, height);
            GUI.Box(detail, "");
            detail.x += 6; detail.y += 6; detail.width -= 12; detail.height -= 12;
            _detailScroll = GUI.BeginScrollView(detail, _detailScroll, new Rect(0, 0, detail.width - 20, _detailHeight));
            float y = 0, width = detail.width - 25;
            bool enabled = GUI.enabled;
            try
            {
                var selected = records.FirstOrDefault(x => x.Key == _selectedKey);
                if (selected == null)
                    Text("Load a normal game to edit character pregnancy.\nCharacter creation uses the Manual shape preview tab.", ref y, width, 68);
                else
                {
                    GUI.enabled = enabled && PregnancyRuntime.Active;
                    DrawCharacter(world, selected, ref y, width);
                }
                GUI.enabled = enabled;
                Text(_message, ref y, width, 48);
                Text("Last event: " + PregnancyRuntime.LastEvent, ref y, width, 90);
                _detailHeight = y + 12;
            }
            finally { GUI.enabled = enabled; GUI.EndScrollView(); }
        }
        finally { GUI.EndGroup(); }
    }

    private static void Select(PregnancyRecord p)
    {
        _selectedKey = p?.Key;
        if (p != null) SyncDay(p);
        else { _dayBuffer = "0"; _observedProgress = -1; _observedDuration = 0; }
        _dayDirty = false;
        _detailScroll = Vector2.zero;
        _message = "Edits stay in memory until you save to a game slot.";
        GUI.FocusControl("");
    }

    private static void DrawCharacter(PregnancyWorld world, PregnancyRecord p, ref float y, float width)
    {
        var rules = PregnancyConfig.Rules;
        double duration = p.GestationDays(rules);
        Text("[ " + p.Name + " ]", ref y, width, 32);
        string state = p.IsPregnant ? $"Pregnant {p.PregnancyProgress:P1}   Growth day {p.PregnancyDay(rules):0.###} / {duration:0.###}"
            : p.RecoveryProgress < 1 && rules.RecoveryDays > 0 ? $"Recovery {p.RecoveryProgress:P1}   ~{p.RecoveryRemainingDays(rules)} days left" : "Not pregnant";
        Text(state, ref y, width, 34);
        float progress = p.IsPregnant ? (float)p.PregnancyProgress : 0;
        var bar = new Rect(0, y, width, 18);
        GUI.Box(bar, "");
        if (progress > 0)
        {
            var oldColor = GUI.color;
            try
            {
                GUI.color = Color.Lerp(new Color(.35f, .78f, .5f), new Color(.93f, .48f, .53f), progress);
                GUI.DrawTexture(new Rect(1, y + 1, (width - 2) * progress, 16), Texture2D.whiteTexture);
            }
            finally { GUI.color = oldColor; }
        }
        y += 25;
        var timing = PregnancyGrowthSchedule.Resolve(rules);
        Text($"Belly shape stage: {p.Growth(rules):P1}\nTiming: {timing.Start:P0} / {timing.Early:P0} / {timing.Middle:P0} / {timing.Late:P0} / 100% (F1)", ref y, width, 48);
        var chance = PregnancyChance.Evaluate(p, rules, PregnancyPlugin.Enabled.Value && PregnancyConfig.Gameplay.Value);
        Text($"Mode: {rules.Mode}   |   F1 base: {chance.BaseChance:P1}", ref y, width, 32);
        if (rules.Mode == ConceptionMode.ComplexCycle)
            Text($"Cycle {p.CycleDay}/28   Ovulation day: {rules.OvulationDay}\nBase {chance.BaseChance:P1} x residual {chance.ResidualFactor:0.###} x egg {chance.EggFactor:0.##}", ref y, width, 46);
        else if (rules.Mode == ConceptionMode.SimpleCycle)
            Text($"Cycle {p.CycleDay}/28 ({rules.Phase(p.CycleDay)})   Safe {rules.SafeChance:P0} / Normal {rules.NormalChance:P0} / Danger {rules.DangerChance:P0}", ref y, width, 44);
        Text($"Effective chance: {chance.EffectiveChance:P1}\n{chance.Reason}", ref y, width, 60);

        bool enabled = GUI.enabled;
        GUI.enabled = enabled && !p.IsPregnant;
        if (GUI.Button(new Rect(0, y, width, 30), "Force Conceive (Debug)")) Edit(world, p, PregnancyDebugCommand.ForceConceive);
        GUI.enabled = enabled;
        y += 35;
        if (GUI.Button(new Rect(0, y, width, 30), "Set Not Pregnant / Clear Recovery")) Edit(world, p, PregnancyDebugCommand.ClearPregnancy);
        y += 37;

        if (p.IsPregnant)
        {
            if (_observedDuration != duration || (!_dayDirty && _observedProgress != p.PregnancyProgress)) SyncDay(p);
            Text($"Base: {duration:0.###} days   Daily variation: +/-{rules.GestationVariation:P1}", ref y, width, 35);
            Text($"Progress {p.PregnancyProgress:0.0000} / 1   Daily +{rules.DailyIncrement(0):0.####} to +{rules.DailyIncrement(1):0.####}", ref y, width, 32);
            float before = (float)p.PregnancyProgress;
            float value = GUI.HorizontalSlider(new Rect(0, y, width, 20), before, 0, 1);
            y += 26;
            if (value != before) Edit(world, p, PregnancyDebugCommand.SetProgress, value);

            GUI.Label(new Rect(0, y, 35, 25), "Day:");
            string text = GUI.TextField(new Rect(38, y, 76, 25), _dayBuffer, 10);
            if (text != _dayBuffer) { _dayBuffer = text; _dayDirty = true; }
            bool valid = double.TryParse(_dayBuffer, NumberStyles.Float, CultureInfo.InvariantCulture, out double typed) && double.IsFinite(typed) && typed >= 0 && typed <= duration;
            GUI.Label(new Rect(118, y, 70, 25), "/ " + duration.ToString("0.###", CultureInfo.InvariantCulture));
            bool narrow = width < 290;
            if (narrow) y += 30;
            GUI.enabled = enabled && valid;
            if (GUI.Button(new Rect(narrow ? 0 : width - 90, y, narrow ? width : 90, 25), "Set Day"))
            {
                if (typed != p.PregnancyDay(rules)) Edit(world, p, PregnancyDebugCommand.SetDay, typed);
                else SyncDay(p);
            }
            GUI.enabled = enabled;
            y += 31;
            if (!valid) Text($"Enter a day from 0 to {duration:0.###} (decimals allowed).", ref y, width, 30);
            int[] changes = { -7, -1, 1, 7 };
            for (int i = 0; i < changes.Length; i++)
                if (GUI.Button(new Rect(i * width / 4, y, width / 4 - 3, 26), (changes[i] > 0 ? "+" : "") + changes[i] + "d"))
                    Edit(world, p, PregnancyDebugCommand.SetDay, Math.Clamp(p.PregnancyDay(rules) + changes[i], 0, duration));
            y += 32;
            string[] labels = { "Day 0", "50%", "Full term" };
            double[] values = { 0, .5, 1 };
            for (int i = 0; i < labels.Length; i++)
                if (GUI.Button(new Rect(i * width / 3, y, width / 3 - 3, 26), labels[i])) Edit(world, p, PregnancyDebugCommand.SetProgress, values[i]);
            y += 33;
            if (p.PregnancyProgress >= 1) Text("Full term: the next game day will process birth.", ref y, width, 38);
        }
        if (!p.IsPregnant && p.RecoveryProgress < 1)
        {
            Text($"Recovery progress: {p.RecoveryProgress:0.0000} / 1\nDaily +{(rules.RecoveryDays > 0 ? 1d / rules.RecoveryDays : 1):0.####}   (1 = recovered)", ref y, width, 46);
            float before = (float)p.RecoveryProgress;
            float value = GUI.HorizontalSlider(new Rect(0, y, width, 20), before, 0, 1);
            y += 26;
            if (value != before) Edit(world, p, PregnancyDebugCommand.SetRecoveryProgress, value);
            if (GUI.Button(new Rect(0, y, width, 28), "Reset Cooldown")) Edit(world, p, PregnancyDebugCommand.ResetCooldown);
            y += 34;
        }
        Text("Father: " + (string.IsNullOrWhiteSpace(p.FatherName) ? "-" : p.FatherName), ref y, width, 30);
        Text($"Births: {p.Births}   |   Recovery remaining: {p.RecoveryRemainingDays(rules)} days", ref y, width, 30);
        if (!PregnancyPlugin.Enabled.Value) Text("Plugin visuals are disabled in F1. State edits still remain in memory.", ref y, width, 48);
        else if (PregnancyController.ManualPreviewActive)
        {
            if (GUI.Button(new Rect(0, y, width, 30), "Stop preview / show pregnancy")) PregnancyController.StopForWorldChange();
            y += 36;
        }
        Text("Changing state / day stops manual preview and updates the belly.\nGlobal probabilities and mode remain in F1.", ref y, width, 55);
    }
    private static void SyncDay(PregnancyRecord p)
    {
        var rules = PregnancyConfig.Rules;
        _dayBuffer = p.PregnancyDay(rules).ToString("0.###", CultureInfo.InvariantCulture);
        _observedProgress = p.PregnancyProgress;
        _observedDuration = p.GestationDays(rules);
        _dayDirty = false;
    }
    private static void Edit(PregnancyWorld world, PregnancyRecord p, PregnancyDebugCommand command, double value = 0)
    {
        if (PregnancyRuntime.ApplyDebug(world, p.Key, command, value))
        {
            SyncDay(p);
            _message = "Changed in memory. Use the game's save screen to keep this state in the slot you choose.";
        }
        else _message = "No change. Check that the game is loaded and the requested state is different.";
    }
    private static void Text(string text, ref float y, float width, float height)
    {
        GUI.Label(new Rect(0, y, width, height), text, _wrapped);
        y += height + 3;
    }
}
