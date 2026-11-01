using PortraitRuntime.Core;

namespace LaunchGiftPortraits.Content;

/// <summary>Content-only bridge: the beauty Mod owns assets; the runtime owns behavior.</summary>
public sealed class BeautyContentMod : IMod
{
    string root;
    public void Init(ModManifest manifest) => root = Path.GetDirectoryName(typeof(BeautyContentMod).Assembly.Location);
    public void OnEnable() => ContentSources.Register("player.launchgiftportraits", Path.Combine(root, "packs"));
    public void OnDisable() => ContentSources.Unregister("player.launchgiftportraits");
}
