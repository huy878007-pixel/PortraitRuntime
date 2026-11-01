using UnityEngine;
using UObj = UnityEngine.Object;

namespace LaunchGiftPortraits.Content;

/// <summary>
/// Drives this Mod's own presentation layer: the F10 hotkey, per-frame offsets and the settings window.
/// The core runtime no longer runs any beauty frame loop.
/// </summary>
public sealed class BeautyPump : MonoBehaviour
{
    public static BeautyPump Instance { get; private set; }
    public DisplaySettings Settings { get; private set; }

    internal void Attach(DisplaySettings settings) { Settings = settings; Instance = this; }

    void Update()
    {
        if (Settings == null) return;
        if (Input.GetKeyDown(Settings.HotkeyCode)) Settings.Toggle();
        Settings.Tick();
    }

    void LateUpdate()
    {
        if (Settings == null) return;
        Settings.CaptureBubble(DialogUIManager.Instance);
        Settings.Apply(PortraitPolicy.LastPath != null);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Settings?.Restore();
    }
}
