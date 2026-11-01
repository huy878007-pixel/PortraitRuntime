using Newtonsoft.Json;
using UnityEngine;

namespace PortraitRuntime.Unity;

public sealed class Settings
{
    public bool Crisp { get; set; } = true;
    public float PortraitScale { get; set; } = 1;
    public float PortraitX { get; set; }
    public float PortraitY { get; set; }
    public float BubbleX { get; set; }
    public float BubbleY { get; set; }
    public float NameX { get; set; }
    public float NameY { get; set; }
    public int CacheMiB { get; set; } = 256;
    public string Hotkey { get; set; } = "F10";
    public string DebugHotkey { get; set; } = "F3";
    public bool DetailedLogging { get; set; } = true;
    [JsonIgnore] public KeyCode DebugHotkeyCode => Enum.TryParse(DebugHotkey, true, out KeyCode value) && (value == KeyCode.F3 || value == KeyCode.F4) ? value : KeyCode.F3;
    [JsonIgnore] public bool HasPortraitAdjustment => PortraitX != 0 || PortraitY != 0 || PortraitScale != 1;
    [JsonIgnore] public KeyCode HotkeyCode => Enum.TryParse(Hotkey, true, out KeyCode value) ? value : KeyCode.F10;
    [JsonIgnore] public bool Open;
    string path;
    Rect window = new(100, 60, 460, 610);
    GameObject blocker;
    Transform portrait, bubble, name;
    Vector3 portraitBase, portraitSize, bubbleBase, nameBase;
    object camera;
    bool savedUpscale, cameraOwned;
    bool portraitOwned;
    public void Load(string file)
    {
        path = file;
        if (File.Exists(file))
        {
            try { JsonConvert.PopulateObject(File.ReadAllText(file), this); }
            catch (JsonException e) { RuntimeMod.Log("Invalid ui.json; using available defaults: " + e.Message); }
        }
        CacheMiB = Math.Max(64, Math.Min(2048, CacheMiB));
        float Clamp(float value, float min, float max, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));
        PortraitScale = Clamp(PortraitScale, .6f, 1.6f, 1);
        PortraitX = Clamp(PortraitX, -300, 300, 0); PortraitY = Clamp(PortraitY, -300, 300, 0);
        BubbleX = Clamp(BubbleX, -600, 600, 0); BubbleY = Clamp(BubbleY, -600, 600, 0);
        NameX = Clamp(NameX, -600, 600, 0); NameY = Clamp(NameY, -600, 600, 0);
        DebugHotkey = DebugHotkeyCode.ToString();
    }
    public void CaptureBubble(DialogUIManager ui)
    {
        if (bubble != ui.clientBubble?.transform) { bubble = ui.clientBubble?.transform; if (bubble != null) bubbleBase = bubble.localPosition; }
        if (name != ui.clientNameBubble?.transform) { name = ui.clientNameBubble?.transform; if (name != null) nameBase = name.localPosition; }
    }
    public void Apply()
    {
        bool anyOpen = Open || RuntimeMod.Current?.Debugger.Open == true;
        if (anyOpen && blocker == null && CustomUIManager.Instance != null)
        {
            var layer = CustomUIManager.Instance.GetLayerRoot("overlay");
            if (layer != null)
            {
                blocker = new GameObject("PortraitRuntime.InputBlocker", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                var rect = (RectTransform)blocker.transform; rect.SetParent(layer, false);
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
                var blockerImage = blocker.GetComponent<UnityEngine.UI.Image>(); blockerImage.color = Color.clear; blockerImage.raycastTarget = true;
            }
        }
        if (!anyOpen && blocker != null) { UnityEngine.Object.Destroy(blocker); blocker = null; }
        var image = RuntimeMod.Current?.Portrait.Renderer ?? StoreClientMono.Instance?.image;
        var target = image != null ? image.transform : null;
        if (target != portrait)
        {
            ResetPortrait();
            portrait = target;
        }
        bool replaced = image != null && image.sprite != null && image.sprite.name.StartsWith("PortraitRuntime/", StringComparison.Ordinal);
        bool moving = StoreClientMono.Instance != null && StoreClientMono.Instance.IsMoving();
        if (portrait != null && replaced && HasPortraitAdjustment && !moving && image.gameObject.activeInHierarchy)
        {
            if (!portraitOwned) { portraitBase = portrait.localPosition; portraitSize = portrait.localScale; }
            portrait.localPosition = portraitBase + new Vector3(PortraitX, PortraitY);
            portrait.localScale = portraitSize * PortraitScale;
            portraitOwned = true;
        }
        else if (portraitOwned) ResetPortrait();
        if (bubble != null && (BubbleX != 0 || BubbleY != 0)) bubble.localPosition = bubbleBase + new Vector3(BubbleX, BubbleY);
        if (name != null && (NameX != 0 || NameY != 0)) name.localPosition = nameBase + new Vector3(NameX, NameY);
        if (EmporiumEntry.Instance == null) { RestoreCamera(); return; }
        var cam = HarmonyLib.AccessTools.Field(typeof(EmporiumEntry), "pixelPerfectCamera")?.GetValue(EmporiumEntry.Instance);
        if (!ReferenceEquals(cam, camera)) { RestoreCamera(); camera = cam; }
        var prop = camera?.GetType().GetProperty("upscaleRT");
        if (prop == null) return;
        if (Crisp && replaced && !cameraOwned) { savedUpscale = (bool)prop.GetValue(camera); if (savedUpscale) prop.SetValue(camera, false); cameraOwned = true; }
        if ((!Crisp || !replaced) && cameraOwned) RestoreCamera();
    }
    void RestoreCamera()
    {
        if (camera is UnityEngine.Object obj && obj == null) { cameraOwned = false; return; }
        if (cameraOwned && camera != null)
        {
            var prop = camera.GetType().GetProperty("upscaleRT");
            if (prop != null && (bool)prop.GetValue(camera) == false) prop.SetValue(camera, savedUpscale);
        }
        cameraOwned = false;
    }
    public void Restore()
    {
        if (blocker != null) UnityEngine.Object.Destroy(blocker);
        blocker = null; Open = false;
        RestoreCamera();
        ResetPortrait();
        if (bubble != null) bubble.localPosition = bubbleBase;
        if (name != null) name.localPosition = nameBase;
    }
    public void ResetPortrait()
    {
        if (portraitOwned && portrait != null) { portrait.localPosition = portraitBase; portrait.localScale = portraitSize; }
        portraitOwned = false;
    }
    public void Draw() => window = GUI.Window(7610, window, Window, "深空内容核心库 · 显示设置");
    public void Save() { if (path != null) File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented)); }
    void Window(int id)
    {
        GUILayout.Label("F3 调试台 · F10 设置 · F11 诊断");
        DebugHotkey = GUILayout.Toggle(DebugHotkey == "F4", "使用 F4 打开调试台（默认 F3）") ? "F4" : "F3";
        Crisp = GUILayout.Toggle(Crisp, "高清立绘");
        void Slider(string label, ref float value, float min, float max) { GUILayout.Label(label + " " + value.ToString("0.00")); value = GUILayout.HorizontalSlider(value, min, max); }
        float s = PortraitScale, x = PortraitX, y = PortraitY, bx = BubbleX, by = BubbleY, nx = NameX, ny = NameY;
        Slider("立绘缩放", ref s, .6f, 1.6f); Slider("立绘 X", ref x, -300, 300); Slider("立绘 Y", ref y, -300, 300);
        Slider("气泡 X", ref bx, -600, 600); Slider("气泡 Y", ref by, -600, 600);
        Slider("姓名框 X", ref nx, -600, 600); Slider("姓名框 Y", ref ny, -600, 600);
        PortraitScale = s; PortraitX = x; PortraitY = y; BubbleX = bx; BubbleY = by; NameX = nx; NameY = ny;
        if (GUILayout.Button("保存")) Save();
        if (GUILayout.Button("重置")) { Restore(); PortraitScale = 1; PortraitX = PortraitY = BubbleX = BubbleY = NameX = NameY = 0; }
        if (GUILayout.Button("关闭")) Open = false;
        GUI.DragWindow(new Rect(0, 0, 460, 25));
    }
}
