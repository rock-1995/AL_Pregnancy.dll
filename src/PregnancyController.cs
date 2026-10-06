using Character;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace ALPregnancy;

public sealed class PregnancyController : MonoBehaviour
{
    private static PregnancyController _instance;
    private readonly List<Human> _humans = new();
    private Human _selected;
    private MeshLease _lease;
    private bool _visible;
    private bool _preview;
    private bool _gameplayTab = true;
    [HideFromIl2Cpp]
    internal static bool ManualPreviewActive => _instance != null && _instance._preview;
    [HideFromIl2Cpp]
    internal static void StopForWorldChange() => _instance?.StopPreview();
    private float _rate = 0.70f;
    private float _nextScan;
    private string _status = "Select a character, then enable Preview.";
    private Rect _window = new(30, 80, 560, 610);
    private Vector2 _scroll;
    private float _contentHeight=1800;
    private GUI.WindowFunction _draw;
    private GUIStyle _statusStyle;

    public PregnancyController(IntPtr pointer) : base(pointer) { _instance = this; }

    public void Update()
    {
        try
        {
            PregnancyPlugin.TickStartupConfig();
            if (MorphReadiness.Ready) FluidMeshLifetime.Collect();
            PregnancyRuntime.Tick();
            if (Input.GetKeyDown(PregnancyPlugin.ToggleKey.Value))
            {
                _visible = !_visible;
                if (_visible) Scan();
                PregnancyPlugin.Logger.LogInfo("Preview panel " + (_visible ? "opened" : "closed"));
            }
            if (!PregnancyPlugin.Enabled.Value)
            {
                if (_preview || _lease != null) StopPreview();
                PregnancyRuntime.ReleaseVisuals();
                return;
            }
            if ((_visible || _preview) && Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 1f;
                Scan();
            }
        }
        catch (Exception ex) { Fail("Character scan", ex); }
    }

    [HideFromIl2Cpp]
    internal static void AfterNativeUpdate(Human human)
    {
        if (_instance == null || human == null || _instance._selected?.Pointer != human.Pointer) return;
        _instance.ApplyPreview();
    }

    [HideFromIl2Cpp]
    private void ApplyPreview()
    {
        if (!_preview || !PregnancyPlugin.Enabled.Value) return;
        try
        {
            if (!Usable(_selected) || _selected.IsReloading) return;
            if (!MorphReadiness.TryBeginApply(_selected)) { _status = MorphReadiness.Status; return; }
            var meshes = MorphReadiness.CurrentMeshes(_selected);
            var changes = _lease?.CheckChanges(meshes) ?? default;
            if (_lease == null || changes.Mesh)
            {
                RestoreMeshes();
                _lease = new MeshLease();
                _lease.Acquire(_selected, meshes);
                PregnancyPlugin.Logger.LogInfo($"Preview meshes: readable={_lease.ReadableCount}, skipped-unreadable={_lease.UnreadableCount}");
            }
            if (changes.Visibility) BellyVertexMorph.Invalidate(Id(_selected));
            BellyVertexMorph.Apply(_selected, Id(_selected), _rate);
            _status = BellyVertexMorph.GetStatusLine(Id(_selected));
            if (!BellyVertexMorph.HasValidBody(Id(_selected)))
            {
                PregnancyPlugin.Logger.LogWarning("Preview stopped: " + _status);
                StopPreview();
                _status = "No compatible readable body/bones. Preview stopped. Use Write diagnostic log.";
            }
        }
        catch (Exception ex) { Fail("Preview", ex); }
    }

    [HideFromIl2Cpp]
    private static int Id(Human human) => human.GameObject.GetInstanceID();

    [HideFromIl2Cpp]
    private static bool Usable(Human human)
    {
        return human != null && !human.Disposed && human.GameObject != null &&
               human.GameObject.activeInHierarchy && human.HiPoly && human.Sex == 1;
    }

    [HideFromIl2Cpp]
    private void Scan()
    {
        PatchCompatibility.Refresh();
        _humans.Clear();
        // Human is now a plain IL2CPP object; use the game's own registry.
        var humans = Human.List;
        if (humans != null)
            for (int i = 0; i < humans.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Human>>().Count; i++)
            {
                Human human = humans[i];
                if (Usable(human)) _humans.Add(human);
            }
        if (_selected != null && (_selected.Disposed || _selected.GameObject == null || !_selected.HiPoly))
        {
            StopPreview();
            _selected = null;
            _status = "Character unloaded. Select a character to preview again.";
        }
    }

    [HideFromIl2Cpp]
    internal static void ReleaseHuman(Human human)
    {
        if (_instance == null || human == null || _instance._selected == null) return;
        if (_instance._selected.Pointer != human.Pointer) return;
        _instance.StopPreview();
        _instance._selected = null;
    }

    [HideFromIl2Cpp]
    internal static void SuspendHuman(Human human)
    {
        if (_instance == null || human == null || _instance._selected?.Pointer != human.Pointer) return;
        _instance.RestoreMeshes();
        _instance._status = "Waiting for native character update";
    }

    [HideFromIl2Cpp]
    private void RestoreMeshes()
    {
        // Undo records before returning the original renderer meshes.
        if (_lease != null && _selected?.GameObject != null) BellyVertexMorph.Forget(Id(_selected));
        _lease?.Dispose();
        _lease = null;
        BodyMeshSelection.Clear();
    }

    [HideFromIl2Cpp]
    public void StopPreview()
    {
        _preview = false;
        RestoreMeshes();
    }

    [HideFromIl2Cpp]
    private void Fail(string phase, Exception ex)
    {
        StopPreview();
        _status = phase + " failed: " + ex.Message;
        PregnancyPlugin.Logger.LogError(_status + "\n" + ex);
    }

    public void OnDestroy()
    {
        StopPreview();
        PregnancyRuntime.Shutdown();
        if (_instance == this) _instance = null;
    }

    public void OnApplicationQuit() => PregnancyRuntime.Shutdown();

    public void OnGUI()
    {
        if (!_visible) return;
        try
        {
            _draw ??= (GUI.WindowFunction)(Action<int>)DrawWindow;
            _window.width = Mathf.Min(_gameplayTab ? 720 : 560, Screen.width - 20);
            _window.height = Mathf.Min(610, Screen.height - 30);
            _window.x = Mathf.Clamp(_window.x, 0, Mathf.Max(0, Screen.width - _window.width));
            _window.y = Mathf.Clamp(_window.y, 0, Mathf.Max(0, Screen.height - _window.height));
            _window = GUI.Window(0x414C50, _window, _draw, "AL Pregnancy 0.2.27 - F8");
        }
        catch (Exception ex)
        {
            _visible = false;
            Fail("Panel", ex);
        }
    }

    [HideFromIl2Cpp]
    private void DrawWindow(int unused)
    {
        float tabWidth = (_window.width - 38) / 2;
        if (GUI.Button(new Rect(14, 26, tabWidth, 25), "Character Debug")) { _gameplayTab = true; _scroll = Vector2.zero; }
        if (GUI.Button(new Rect(24 + tabWidth, 26, tabWidth, 25), "Manual shape preview")) { _gameplayTab = false; _scroll = Vector2.zero; }
        if (GUI.Button(new Rect(_window.width - 38, 3, 28, 20), "X")) _visible = false;
        var area = new Rect(12, 55, _window.width - 24, _window.height - 70);
        if (_gameplayTab)
        {
            PregnancyPanel.Draw(area);
            GUI.DragWindow(new Rect(0, 0, _window.width - 40, 24));
            return;
        }
        _scroll = GUI.BeginScrollView(area, _scroll, new Rect(0, 0, area.width - 20, _contentHeight));
        float width = area.width - 26;
        float y = 0;
        GUI.Label(new Rect(0, y, width, 24), "Manual preview does not change pregnancy or saved state."); y += 28;
        GUI.Label(new Rect(0, y, width, 30), PatchCompatibility.Status); y += 34;
        GUI.Label(new Rect(0, y, width, 24), $"Active female characters (high detail): {_humans.Count}"); y += 28;
        foreach (Human human in _humans.ToArray())
        {
            bool selected = _selected != null && _selected.Pointer == human.Pointer;
            string label = (selected ? "> " : "") + NameOf(human) + "  [" + Id(human) + "]";
            if (GUI.Button(new Rect(0, y, width, 26), label))
            {
                StopPreview();
                _selected = human;
                _status = "Selected. Preview is OFF.";
                PregnancyPlugin.Logger.LogInfo("Selected " + label);
            }
            y += 29;
        }
        if (_humans.Count == 0)
        {
            GUI.Label(new Rect(0, y, width, 42), "Open a character in the character creator or a scene.\nOnly visible high-detail female characters are listed."); y += 46;
        }
        GUI.enabled = _selected != null && PregnancyPlugin.Enabled.Value;
        if (GUI.Button(new Rect(0, y, width / 2 - 4, 30), _preview ? "Preview: ON (click to stop)" : "Enable Preview"))
        {
            if (_preview) StopPreview();
            else { PregnancyRuntime.ReleaseVisuals(); _preview = true; }
        }
        GUI.enabled = _selected != null;
        if (GUI.Button(new Rect(width / 2 + 4, y, width / 2 - 4, 30), "Reset / restore meshes"))
        {
            StopPreview();
            _status = "Original meshes restored. Preview is OFF.";
        }
        y += 38;
        _rate = Slider("Growth stage", _rate, 0, 1, ref y, width);
        VtxSettings p = BellyDeformSettings.Vtx;
        p.GrowthFullness = Slider("Forward fullness", p.GrowthFullness, 0.5f, 1.6f, ref y, width);
        p.GrowthWidth = Slider("Belly width", p.GrowthWidth, 0.5f, 2f, ref y, width);
        p.UpperReach = Slider("Upper abdomen reach", p.UpperReach, 0.75f, 1.2f, ref y, width);
        p.VerticalRange = Slider("Vertical influence range", p.VerticalRange, .6f, 1.2f, ref y, width);
        p.WallSmoothing = Slider("Whole-abdomen smoothing", p.WallSmoothing, 0, 2, ref y, width);
        p.SagStrength = Slider("Belly sag (0 = off)", p.SagStrength, 0, 2, ref y, width);
        p.MidVolume = Slider("Second-stage volume", p.MidVolume, .75f, 1.5f, ref y, width);
        p.LowerPoleLift = Slider("Late lower-pole lift", p.LowerPoleLift, 0, 2, ref y, width);
        p.SkinClearance = Slider("Skin clearance", p.SkinClearance, 0, .2f, ref y, width);
        p.LateSettle = Slider("Late settling", p.LateSettle, 0, 1, ref y, width);
        p.ClothOffset = Slider("Clothing displacement", p.ClothOffset, 0.8f, 1.3f, ref y, width);
        p.ClothTopMult = p.ClothBotMult = p.ClothBraMult = p.ClothShortsMult = p.ClothPanstMult = p.ClothOtherMult = p.ClothOffset;
        GUI.Label(new Rect(0,y,width,24),"Virtual axis / pose response");y+=28;
        p.VirtualAxisStrength=Slider("Virtual axis strength (0 = native)",p.VirtualAxisStrength,0,1,ref y,width);
        p.AxisBlendStart=Slider("Blend start / torso span",p.AxisBlendStart,0,.15f,ref y,width);
        p.AxisBlendFull=Slider("Full blend / torso span",p.AxisBlendFull,.05f,.6f,ref y,width);
        GUI.Label(new Rect(0,y,width,24),"Lower attachment / original body height");y+=28;
        p.LowerTransitionStart=Slider("Lower start above pelvic floor / span",p.LowerTransitionStart,-.50f,.75f,ref y,width);
        p.LowerTransitionWidth=Slider("Lower transition width / span",p.LowerTransitionWidth,.02f,1.50f,ref y,width);
        p.LowerTransitionBias=Slider("Lower curve bias (- earlier / + later)",p.LowerTransitionBias,-2,2,ref y,width);
        GUI.Label(new Rect(0,y,width,24),"Upper release / original body height");y+=28;
        p.UpperTransitionStart=Slider("Upper start above waist datum / span",p.UpperTransitionStart,-.50f,1f,ref y,width);
        p.UpperTransitionWidth=Slider("Upper transition width / span",p.UpperTransitionWidth,.02f,1.50f,ref y,width);
        p.UpperTransitionBias=Slider("Upper curve bias (- hold / + release)",p.UpperTransitionBias,-2,2,ref y,width);
        p.UpperTransitionJoin=Slider("Upper join / transition width",p.UpperTransitionJoin,.02f,1f,ref y,width);
        p.UpperTransitionActivation=Slider("Upper activation displacement / span",p.UpperTransitionActivation,.001f,.15f,ref y,width);
        GUI.Label(new Rect(0,y,width,24),"Additional restrictions (trial defaults: OFF)");y+=28;
        p.BreastExclusionEnabled=Toggle("Exclude breast-weighted vertices",p.BreastExclusionEnabled,ref y,width);
        p.UpperBoneFilterEnabled=Toggle("Upper abdomen bone-weight filter",p.UpperBoneFilterEnabled,ref y,width);
        p.UpperFieldFadeEnabled=Toggle("Extra shape-field top fade",p.UpperFieldFadeEnabled,ref y,width);
        p.AxisPullLow=Slider("Pull at small angles",p.AxisPullLow,0,1,ref y,width);
        p.AxisPullHigh=Slider("Pull at large angles",p.AxisPullHigh,0,1,ref y,width);
        p.AxisPullAngle=Slider("Full-pull angle (degrees)",p.AxisPullAngle,10,120,ref y,width);
        p.AxisAnchorY=Slider("Anchor height / torso span",p.AxisAnchorY,-.2f,.2f,ref y,width);
        p.AxisAnchorZ=Slider("Anchor forward / torso span",p.AxisAnchorZ,-.2f,.2f,ref y,width);
        p.SkinShadingSmoothing=Slider("Skin lighting smoothing (geometry unchanged)",p.SkinShadingSmoothing,0,1,ref y,width);
        GUI.Label(new Rect(0,y,width,24),"Original navel / local geometry");y+=28;
        GUI.Label(new Rect(0,y,width,24),_selected==null?"Select a character to detect original navel":BellyVertexMorph.GetNavelStatus(Id(_selected)));y+=28;
        bool fullNavel=GUI.Toggle(new Rect(0,y,width,24),p.NavelPreviewFull,"Preview full navel response at current belly size");y+=28;
        if(fullNavel!=p.NavelPreviewFull){p.NavelPreviewFull=fullNavel;BellyVertexMorph.InvalidateAll();}
        GUI.Label(new Rect(0,y,width,24),$"Navel stage response: {BellyShape.NavelStageResponse(_rate,p)*100:F1}% (geometry only)");y+=28;
        if(GUI.Button(new Rect(0,y,width/2-4,28),"Navel visible preset"))
        {
            p.NavelPreviewFull=true;p.NavelEversion=1;p.NavelHeight=.012f;p.NavelRadius=.06f;p.NavelProportion=.85f;
            BellyVertexMorph.InvalidateAll();
        }
        if(GUI.Button(new Rect(width/2+4,y,width/2-4,28),"Navel off"))
        {p.NavelEversion=0;p.NavelProportion=0;BellyVertexMorph.InvalidateAll();}
        y+=35;
        p.NavelEversion=Slider("Navel eversion (0 = off)",p.NavelEversion,0,2,ref y,width);
        p.NavelStart=Slider("Navel change starts at stage",p.NavelStart,.3f,.95f,ref y,width);
        p.NavelHeight=Slider("Navel height / torso span",p.NavelHeight,0,.03f,ref y,width);
        p.NavelRadius=Slider("Navel patch radius / torso span",p.NavelRadius,.015f,.08f,ref y,width);
        p.NavelProportion=Slider("Navel proportion retention",p.NavelProportion,0,1,ref y,width);
        if (GUI.Button(new Rect(0, y, width / 2 - 4, 28), "Default shape"))
        {
            BellyDeformSettings.ResetToDefaults();
            BellyVertexMorph.InvalidateAll();
        }
        if (GUI.Button(new Rect(width / 2 + 4, y, width / 2 - 4, 28), "Write diagnostic log")) Dump();
        y += 35;
        if (GUI.Button(new Rect(0, y, width / 2 - 4, 28), "Full-term shape"))
        {
            BellyDeformSettings.ResetToDefaults();
            _rate=1f;
            BellyVertexMorph.InvalidateAll();
        }
        if (GUI.Button(new Rect(width / 2 + 4, y, width / 2 - 4, 28), "Rebuild shape")) BellyVertexMorph.InvalidateAll();
        GUI.enabled = true;
        y += 35;
        if (_statusStyle == null)
        {
            _statusStyle = new GUIStyle { wordWrap = true, fontSize = 14 };
            _statusStyle.normal.textColor = Color.white;
        }
        GUI.Label(new Rect(0, y, width, 72), _status, _statusStyle); y += 76;
        if (_lease != null)
            GUI.Label(new Rect(0, y, width, 48), $"Owned meshes: {_lease.ReadableCount}; unreadable skipped: {_lease.UnreadableCount}\nUnreadable clothing is not deformed in this build.", _statusStyle);
        _contentHeight=y+68;
        GUI.EndScrollView();
        GUI.DragWindow(new Rect(0, 0, _window.width - 45, 23));
    }

    [HideFromIl2Cpp]
    private static string NameOf(Human human)
    {
        try { return human.FileParam?.fullname ?? human.Name ?? "Character"; }
        catch { return "Character"; }
    }

    [HideFromIl2Cpp]
    private float Slider(string label, float value, float min, float max, ref float y, float width)
    {
        GUI.Label(new Rect(0, y, width, 24), $"{label}: {value:F3}");
        float next = GUI.HorizontalSlider(new Rect(0, y + 25, width, 20), value, min, max);
        y += 49;
        if (!Mathf.Approximately(value, next)) BellyVertexMorph.InvalidateAll();
        return next;
    }

    [HideFromIl2Cpp]
    private bool Toggle(string label, bool value, ref float y, float width)
    {
        bool next = GUI.Toggle(new Rect(0, y, width, 24), value, label);
        y += 28;
        if (value != next) BellyVertexMorph.InvalidateAll();
        return next;
    }

    [HideFromIl2Cpp]
    private void Dump()
    {
        PatchCompatibility.Refresh(forceLog: true);
        if (_selected == null) return;
        BellyVertexMorph.DumpInfo(_selected, Id(_selected));
        foreach (var smr in _selected.GameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null || smr.sharedMesh == null) continue;
            Mesh mesh = smr.sharedMesh;
            PregnancyPlugin.Logger.LogInfo($"Mesh: renderer={smr.name}, mesh={mesh.name}, readable={mesh.isReadable}, vertices={mesh.vertexCount}, submeshes={mesh.subMeshCount}, blendshapes={mesh.blendShapeCount}");
        }
        _status = "Diagnostics written to LogOutput.log and AL_Pregnancy/diagnostics/latest-mesh.json";
    }
}


