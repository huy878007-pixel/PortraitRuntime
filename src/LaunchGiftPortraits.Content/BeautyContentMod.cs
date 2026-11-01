using UnityEngine;
using UObj = UnityEngine.Object;

namespace LaunchGiftPortraits.Content;

/// <summary>
/// Content-only Mod: this package owns artwork, mapping, presentation policy and settings;
/// the core runtime owns loading, lifecycle notification and the renderer mechanism.
/// No compile-time reference to the core: see <see cref="CoreBridge"/>.
/// </summary>
public sealed class BeautyContentMod : IMod
{
    static DisplaySettings settings;
    GameObject host;
    ModManifest manifest;

    public static DisplaySettings Settings => settings;

    // Native ModLoader owns enable state. The core discovers this Mod's opt-in marker.
    public void Init(ModManifest manifest) { this.manifest = manifest; }

    public void OnEnable()
    {
        if (!CoreBridge.Bind())
        {
            Warn("Core runtime not reachable; portrait presentation disabled.");
            return;
        }
        settings = LoadSettings();
        // This package answers every sprite and item replacement the core asks about, and owns the
        // scene specials the core no longer knows about.
        SpritePresenter.Bind();
        SpecialTargets.Refresh();
        // The frame host needs a live engine. If it cannot be created the bridge stays bound and the
        // Mod simply stops presenting, instead of taking the game's mod loader down with it.
        try
        {
            host = new GameObject("LaunchGiftPortraits.Lifecycle");
            UObj.DontDestroyOnLoad(host);
            host.AddComponent<BeautyPump>().Attach(settings);
        }
        catch (Exception e)
        {
            host = null;
            Warn("Presentation host unavailable; behaviour only. " + e.GetType().Name);
        }
        Warn("Enabled; " + Diagnostic());
    }

    public void OnDisable()
    {
        SpecialTargets.Restore();
        SpritePresenter.Unbind();
        if (host != null) { UObj.Destroy(host); host = null; }
        settings?.Restore();
        CoreBridge.Restore();
        CoreBridge.Unbind();
        settings = null;
    }

    /// <summary>
    /// ui.json lives next to this DLL. The legacy core build kept the same values in the core folder;
    /// that file is read once, copied aside, and never overwritten.
    /// </summary>
    static DisplaySettings LoadSettings()
    {
        var folder = Path.GetDirectoryName(typeof(BeautyContentMod).Assembly.Location) ?? ".";
        var own = Path.Combine(folder, "ui.json");
        var legacy = CoreBridge.CoreFolder == null ? null : Path.Combine(CoreBridge.CoreFolder, "ui.json");
        var settings = new DisplaySettings();
        if (File.Exists(own) || legacy == null || !File.Exists(legacy)) { settings.Load(own); return settings; }
        try
        {
            settings.Load(legacy);
            var backup = legacy + ".core-legacy.bak";
            if (!File.Exists(backup)) File.Copy(legacy, backup, false);
            settings.Save(own);
            Warn("Migrated presentation settings from the core build into " + own);
        }
        catch (Exception e)
        {
            Warn("Legacy ui.json migration failed, keeping defaults: " + e.Message);
            settings.Load(own);
        }
        return settings;
    }

    /// <summary>Diagnostics for the runtime log and the offline contract test.</summary>
    public static string Diagnostic() => "bound=" + CoreBridge.Bound + " version=" + CoreBridge.Version
        + " subscribers=" + CoreBridge.Subscribers + " pump=" + (BeautyPump.Instance != null) + " last=" + CoreBridge.LastEvent;
    public static bool BridgeReady => CoreBridge.Bound;
    public static string LastEvent => CoreBridge.LastEvent;

    static void Warn(string text) { try { Debug.Log("[LaunchGiftPortraits] " + text); } catch (Exception) { } }
}
