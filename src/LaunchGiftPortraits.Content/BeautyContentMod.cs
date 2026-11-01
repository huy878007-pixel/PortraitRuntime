namespace LaunchGiftPortraits.Content;

/// <summary>Content-only bridge: the beauty Mod owns assets; the runtime owns behavior.</summary>
public sealed class BeautyContentMod : IMod
{
    // Native ModLoader owns enable state. The core discovers this Mod's opt-in marker.
    public void Init(ModManifest manifest) { }
    public void OnEnable() { }
    public void OnDisable() { }
}
