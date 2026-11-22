using Character;
using CharacterCreation;
using CharacterCreation.UI;
using CharacterCreation.UI.View.Profile;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ALPregnancy;

// Reuse the game's slider/input/reset and undo bindings. Editing changes only the
// current HumanData; the external pregnancy world owns gameplay persistence.
internal static class PregnancyMaker
{
    // Native InputSliderButton(2Line) occupies 42 canvas units. A hidden
    // personality control has no reliable laid-out height to copy.
    private const float RowHeight = 42;
    private const float RowSpacing = 4;
    private const float PanelPadding = 12;
    private static readonly Dictionary<IntPtr, Entry> Entries = new();
    private static PendingCommit _pendingCommit;

    private sealed class PendingCommit
    {
        // Strong native references keep these identities valid until the game's
        // OnDestroy callback copies the accepted snapshot into the source NPC.
        internal HumanData Source, Snapshot;
        internal bool Matches(HumanData destination, HumanData source)
            => Source?.Pointer == destination?.Pointer && Snapshot?.Pointer == source?.Pointer;
    }

    private sealed class Entry
    {
        internal PersonalityWindow Window;
        internal ProfileView View;
        internal CategorySelection Selection;
        internal CategoryKindToggle Tab;
        internal GameObject Panel, HiddenRoot;
        internal UnityAction<bool> TabChanged;
        internal int LastNativeIndex;
        internal InputSliderButton Belly, Body;
        internal HumanCustom.IInputSlider.Get BellyGet, BodyGet;
        internal HumanCustom.IInputSlider.Set BellySet, BodySet;
        internal HumanCustom.IInputSlider.Reset BellyReset, BodyReset;
        internal bool Submitted;

        internal HumanDataCustom Custom => Window?._human?.Data?.Custom;

        internal float Read(bool belly)
        {
            var data = Custom;
            if (data == null) return 0;
            var values = PregnancyAppearance.Get(data);
            return belly ? values.Belly : values.Body;
        }

        internal bool Write(bool belly, float value)
        {
            var data = Custom;
            if (data == null) return false;
            var values = PregnancyAppearance.Get(data);
            float before = belly ? values.Belly : values.Body;
            if (belly) values.SetBelly(value); else values.SetBody(value);
            bool changed = before != (belly ? values.Belly : values.Body);
            if (changed) PregnancyRuntime.OnAppearanceEdited(Window._human, values);
            return changed;
        }

        internal void Refresh()
        {
            bool female = Window?._human?.Sex == 1;
            if (Tab != null)
            {
                Tab.Title = "妊娠";
                Tab.Active = female;
                if (!female) Hide(true);
            }
            if (Belly != null) { Belly.Active = female; if (female) Belly.Set(); }
            if (Body != null) { Body.Active = female; if (female) Body.Set(); }
        }

        internal void Show()
        {
            if (Panel == null || HiddenRoot != null) return;
            var nativeRoot = Selection?._view?._targetPair?.Root;
            if (nativeRoot == null) return;
            // Native category indices remain untouched. Restore this root before
            // the next native OpenView, including selecting the same native tab.
            HiddenRoot = nativeRoot.gameObject;
            HiddenRoot.SetActive(false);
            if (Tab.Toggle.group == null)
                foreach (var toggle in Selection._toggles) toggle.SetIsOnWithoutNotify(false);
            Panel.SetActive(true);
            Refresh();
        }

        internal void Hide(bool restoreSelection)
        {
            if (Panel != null) Panel.SetActive(false);
            if (HiddenRoot != null) HiddenRoot.SetActive(true);
            HiddenRoot = null;
            if (Tab != null) Tab.Toggle.SetIsOnWithoutNotify(false);
            var toggles = Selection?._toggles;
            if (!restoreSelection || toggles == null || toggles.Length == 0) return;
            foreach (var toggle in toggles) if (toggle.isOn) return;
            toggles[Math.Clamp(LastNativeIndex, 0, toggles.Length - 1)].SetIsOnWithoutNotify(true);
        }

        internal void Dispose()
        {
            Hide(true);
            if (Tab != null)
            {
                Tab.Toggle.onValueChanged.RemoveListener(TabChanged);
                UnityEngine.Object.Destroy(Tab.gameObject);
            }
            Destroy(Belly);
            Destroy(Body);
            if (Panel != null) UnityEngine.Object.Destroy(Panel);
            Belly = Body = null;
            Tab = null;
            Panel = null;
        }
    }

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(PersonalityWindow), nameof(PersonalityWindow.Initialize)),
            postfix: new HarmonyMethod(typeof(PregnancyMaker), nameof(AfterInitialize)));
        harmony.Patch(AccessTools.Method(typeof(PersonalityWindow), nameof(PersonalityWindow.Refresh)),
            postfix: new HarmonyMethod(typeof(PregnancyMaker), nameof(AfterRefresh)));
        harmony.Patch(AccessTools.Method(typeof(PersonalityWindow), nameof(PersonalityWindow.Dispose)),
            prefix: new HarmonyMethod(typeof(PregnancyMaker), nameof(BeforeDispose)));
        harmony.Patch(AccessTools.Method(typeof(ProfileView), nameof(ProfileView.UpdateCustomUI)),
            postfix: new HarmonyMethod(typeof(PregnancyMaker), nameof(AfterProfileUpdate)));
        harmony.Patch(AccessTools.Method(typeof(CategorySelection), nameof(CategorySelection.OpenView)),
            prefix: new HarmonyMethod(typeof(PregnancyMaker), nameof(BeforeOpenNative)),
            postfix: new HarmonyMethod(typeof(PregnancyMaker), nameof(AfterOpenNative)));
        harmony.Patch(AccessTools.Method(typeof(CategorySelection), nameof(CategorySelection.Close)),
            prefix: new HarmonyMethod(typeof(PregnancyMaker), nameof(BeforeCloseCategory)));
    }

    internal static bool IsEditing(Human human)
    {
        if (human == null) return false;
        foreach (var entry in Entries.Values)
            if (entry.Window?._human?.Pointer == human.Pointer) return true;
        return false;
    }

    // Some game/editor integrations pass an existing HumanData to the maker.
    // This exact source pointer can identify an NPC; a card name cannot.
    internal static HumanData SourceData(Human human)
    {
        if (human == null) return null;
        foreach (var entry in Entries.Values)
            if (entry.Window?._human?.Pointer == human.Pointer)
                return entry.Window._customScene?.Received?.HumanData;
        return null;
    }

    private static void AfterInitialize(PersonalityWindow __instance, BaseUI __0)
    {
        if (__instance == null || __instance._pitchPow == null) return;
        BeforeDispose(__instance);
        // A new editor is a new transaction. A cancelled/abandoned old editor
        // cannot leave an accepted snapshot pending for an unrelated session.
        _pendingCommit = null;
        var entry = new Entry { Window = __instance, View = __0?.TryCast<ProfileView>() };
        try
        {
            entry.BellyGet = new Func<float>(() => entry.Read(true));
            entry.BodyGet = new Func<float>(() => entry.Read(false));
            entry.BellySet = new Func<float, bool>(value => entry.Write(true, value));
            entry.BodySet = new Func<float, bool>(value => entry.Write(false, value));
            entry.BellyReset = new Func<HumanData, float>(data => data?.Custom == null ? 0 : PregnancyAppearance.Get(data.Custom).Belly);
            entry.BodyReset = new Func<HumanData, float>(data => data?.Custom == null ? 0 : PregnancyAppearance.Get(data.Custom).Body);
            Entries.Add(__instance.Pointer, entry);
            PregnancyRuntime.InitializeMakerAppearance(__instance._human);
            TryAttachTab(entry);
        }
        catch (Exception ex)
        {
            Entries.Remove(__instance.Pointer);
            entry.Dispose();
            PregnancyPlugin.Logger.LogError("[Pregnancy] Maker appearance sliders: " + ex);
        }
    }

    private static void TryAttachTab(Entry entry)
    {
        if (entry.Tab != null || entry.View == null) return;
        var kinds = entry.View._kindToggles;
        var selections = entry.Window._customScene?.SelectionTop?.Selections;
        if (kinds == null || selections == null) return;
        int count = kinds.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<CategoryKindToggle>>().Count;
        if (count == 0) return;
        CategorySelection selection = null;
        foreach (var candidate in selections)
        {
            var list = candidate?.KindToggles;
            if (list != null && candidate._toggles?.Length > 0 && list[0].Pointer == kinds[0].Pointer)
            { selection = candidate; break; }
        }
        var root = selection?._view?._targetPair?.Root;
        if (root == null) return;
        entry.Selection = selection;
        for (int i = 0; i < selection._toggles.Length; ++i)
            if (selection._toggles[i].isOn) entry.LastNativeIndex = i;

        var panel = new GameObject("ALPregnancyPanel", new Il2CppReferenceArray<Il2CppSystem.Type>(
            new[] { Il2CppType.Of<RectTransform>() }));
        entry.Panel = panel;
        panel.SetActive(false);
        var panelRect = panel.transform.Cast<RectTransform>();
        panelRect.SetParent(root.parent, false);
        CopyRect(root.Cast<RectTransform>(), panelRect);
        var categoryEdit = entry.View.CategoryEdit;
        var cachedWindow = categoryEdit.__parameterWindowCache;
        var nativeObject = cachedWindow != null ? cachedWindow.gameObject : null;
        if (nativeObject == null && categoryEdit._windowTable != null)
            categoryEdit._windowTable.TryGetValue(CategoryEdit.Type.Parameter, out nativeObject);
        bool nativeWasActive = nativeObject != null && nativeObject.activeSelf;
        // The getter also reuses uncached entries in the window table. Preserve
        // existing visibility and hide a window created only to obtain templates.
        var nativeWindow = categoryEdit.ParameterWindow;
        if (nativeWindow.gameObject.activeSelf != nativeWasActive)
            nativeWindow.gameObject.SetActive(nativeWasActive);
        var nativeBackground = nativeWindow._imgBG;
        var backgroundObject = new GameObject("Background", new Il2CppReferenceArray<Il2CppSystem.Type>(
            new[] { Il2CppType.Of<RectTransform>(), Il2CppType.Of<Image>() }));
        var backgroundRect = backgroundObject.transform.Cast<RectTransform>();
        backgroundRect.SetParent(panelRect, false);
        backgroundRect.anchorMin = new Vector2(0, 1);
        backgroundRect.anchorMax = new Vector2(1, 1);
        backgroundRect.pivot = new Vector2(0.5f, 1);
        backgroundRect.anchoredPosition = Vector2.zero;
        backgroundRect.sizeDelta = new Vector2(0, PanelPadding * 2 + RowHeight * 2 + RowSpacing);
        var background = backgroundObject.GetComponent<Image>();
        background.sprite = nativeBackground.sprite;
        background.color = nativeBackground.color;
        background.material = nativeBackground.material;
        background.type = nativeBackground.type;
        background.pixelsPerUnitMultiplier = nativeBackground.pixelsPerUnitMultiplier;
        background.raycastTarget = true;

        var original = nativeWindow._inputSliderButtonPool._original;
        entry.Belly = Create(original, backgroundRect, "ALPregnancyBelly", "お腹", 0,
            entry.BellyGet, entry.BellySet, entry.BellyReset);
        entry.Body = Create(original, backgroundRect, "ALPregnancyBody", "体型", 1,
            entry.BodyGet, entry.BodySet, entry.BodyReset);

        var last = kinds[count - 1];
        var template = kinds[0];
        for (int i = 0; i < count; ++i)
            if (!kinds[i].Toggle.isOn) { template = kinds[i]; break; }
        var tab = UnityEngine.Object.Instantiate(template, last.transform.parent, false);
        entry.Tab = tab;
        tab.gameObject.name = "ALPregnancyTab";
        tab.Toggle.onValueChanged = new Toggle.ToggleEvent();
        tab.Toggle.SetIsOnWithoutNotify(false);
        tab.Title = "妊娠";
        tab.transform.SetSiblingIndex(last.transform.GetSiblingIndex() + 1);
        if (last.transform.parent.GetComponent<LayoutGroup>() == null)
        {
            var lastRect = last.transform.Cast<RectTransform>();
            var step = count > 1
                ? lastRect.anchoredPosition - kinds[count - 2].transform.Cast<RectTransform>().anchoredPosition
                : Vector2.down * (lastRect.rect.height + 4);
            tab.transform.Cast<RectTransform>().anchoredPosition = lastRect.anchoredPosition + step;
        }
        entry.TabChanged = new Action<bool>(isOn => { if (isOn) entry.Show(); else entry.Hide(true); });
        tab.Toggle.onValueChanged.AddListener(entry.TabChanged);
        entry.Refresh();
        LayoutRebuilder.MarkLayoutForRebuild(last.transform.parent.Cast<RectTransform>());
        PregnancyPlugin.Logger.LogInfo("[Pregnancy] Added maker pregnancy tab.");
    }

    private static void CopyRect(RectTransform source, RectTransform destination)
    {
        destination.anchorMin = source.anchorMin;
        destination.anchorMax = source.anchorMax;
        destination.pivot = source.pivot;
        destination.sizeDelta = source.sizeDelta;
        destination.anchoredPosition = source.anchoredPosition;
        destination.localScale = source.localScale;
        destination.localRotation = source.localRotation;
    }

    private static InputSliderButton Create(InputSliderButton original, Transform parent, string name, string title, int row,
        HumanCustom.IInputSlider.Get get, HumanCustom.IInputSlider.Set set, HumanCustom.IInputSlider.Reset reset)
    {
        var slider = UnityEngine.Object.Instantiate(original, parent, false);
        try
        {
            slider.gameObject.name = name;
            slider.gameObject.SetActive(false);
            // The cloned serialized controls are distinct. Native Initialize creates
            // fresh subscriptions, including text entry, Reset and Undo/Redo.
            slider.Title = title;
            slider.Slider.minValue = 0;
            slider.Slider.maxValue = 1;
            slider.Slider.wholeNumbers = false;
            // Persistent UnityEvent calls are serialized with a cloned prefab.
            // Replace the clone's value/reset events before native Initialize
            // installs its own undo/input listeners, so pitch callbacks cannot run.
            slider.Slider.onValueChanged = new Slider.SliderEvent();
            slider.Input.onValueChanged = new TMPro.TMP_InputField.OnChangeEvent();
            slider.Input.onEndEdit = new TMPro.TMP_InputField.SubmitEvent();
            slider.Button.onClick = new Button.ButtonClickedEvent();
            slider.UseUndo = true;
            slider.Initialize(get, set, reset);
            var fitter = slider.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
            var rect = slider.transform.Cast<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(-PanelPadding * 2, RowHeight);
            rect.anchoredPosition = new Vector2(PanelPadding, -PanelPadding - row * (RowHeight + RowSpacing));
            slider.gameObject.SetActive(true);
            return slider;
        }
        catch { Destroy(slider); throw; }
    }

    private static void AfterProfileUpdate(ProfileView __instance)
    {
        foreach (var entry in Entries.Values)
            if (entry.View?.Pointer == __instance.Pointer) { TryAttachTab(entry); entry.Refresh(); }
    }

    private static void BeforeOpenNative(CategorySelection __instance, int __0)
    {
        foreach (var entry in Entries.Values)
            if (entry.Selection?.Pointer == __instance.Pointer)
            { entry.Hide(false); entry.LastNativeIndex = __0; }
    }

    private static void AfterOpenNative()
    {
        foreach (var entry in Entries.Values) TryAttachTab(entry);
    }

    private static void BeforeCloseCategory(CategorySelection __instance)
    {
        foreach (var entry in Entries.Values)
            if (entry.Selection?.Pointer == __instance.Pointer) entry.Hide(true);
    }

    private static void AfterRefresh(PersonalityWindow __instance)
    {
        if (__instance != null && Entries.TryGetValue(__instance.Pointer, out var entry)) entry.Refresh();
    }

    private static void BeforeDispose(PersonalityWindow __instance)
    {
        if (__instance == null || !Entries.Remove(__instance.Pointer, out var entry)) return;
        // Personality UI can be disposed before the outer scene's OnDestroy
        // subscriber submits EditHumanData. Retain only a real accepted snapshot.
        _pendingCommit = CaptureCommit(entry);
        entry.Dispose();
    }

    private static PendingCommit CaptureCommit(Entry entry)
    {
        if (entry.Submitted) return null;
        var scene = entry.Window?._customScene;
        var source = scene?.Received?.HumanData;
        var snapshot = scene?.EditHumanData;
        return source != null && snapshot != null ? new PendingCommit { Source = source, Snapshot = snapshot } : null;
    }

    internal static void OnDataCopied(HumanData destination, HumanData source, AppearanceValues copiedValues)
    {
        if (destination == null || source == null || copiedValues == null) return;
        // Both identities are required: internal undo/copy operations and loading
        // another card must not commit the current slider preview to the NPC.
        PendingCommit commit = _pendingCommit?.Matches(destination, source) == true ? _pendingCommit : null;
        Entry submittedEntry = null;
        if (commit == null)
            foreach (var entry in Entries.Values)
            {
                var active = CaptureCommit(entry);
                if (active?.Matches(destination, source) == true) { commit = active; submittedEntry = entry; break; }
            }
        if (commit == null) return;
        PregnancyRuntime.CommitMakerAppearance(destination, copiedValues);
        if (submittedEntry != null) submittedEntry.Submitted = true;
        _pendingCommit = null;
    }

    private static void Destroy(InputSliderButton slider)
    {
        if (slider == null) return;
        slider.Dispose();
        UnityEngine.Object.Destroy(slider.gameObject);
    }

    internal static void Shutdown()
    {
        foreach (var entry in Entries.Values) entry.Dispose();
        Entries.Clear();
        _pendingCommit = null;
    }
}
