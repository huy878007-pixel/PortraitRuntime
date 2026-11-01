using Newtonsoft.Json;
using UnityEngine;

namespace PortraitRuntime.Unity;

/// <summary>
/// Runtime settings that belong to the core: cache budget, debug key and log verbosity.
/// Display preferences (offset, scale, crispness, the F10 window) belong to the content Mod.
/// </summary>
public sealed class Settings
{
    public int CacheMiB { get; set; } = 256;
    public string DebugHotkey { get; set; } = "F3";
    public bool DetailedLogging { get; set; } = true;
    [JsonIgnore] public KeyCode DebugHotkeyCode => Enum.TryParse(DebugHotkey, true, out KeyCode value) && (value == KeyCode.F3 || value == KeyCode.F4) ? value : KeyCode.F3;
    string path;

    /// <summary>Load runtime.json. An existing legacy ui.json is read once so a user's choices survive the split.</summary>
    public void Load(string file, string legacy = null)
    {
        path = file;
        var source = File.Exists(file) ? file : legacy != null && File.Exists(legacy) ? legacy : null;
        if (source != null)
        {
            try { JsonConvert.PopulateObject(File.ReadAllText(source), this); }
            catch (JsonException e) { RuntimeMod.Log("Invalid runtime settings; using available defaults: " + e.Message); }
        }
        Normalize();
        if (source != null && source != file) Save();
    }
    public void Normalize()
    {
        CacheMiB = Math.Max(64, Math.Min(2048, CacheMiB));
        DebugHotkey = DebugHotkeyCode.ToString();
    }
    public void Save()
    {
        if (path == null) return;
        try { File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented)); }
        catch (Exception e) { RuntimeMod.Log("Cannot save runtime settings: " + e.Message); }
    }
}
