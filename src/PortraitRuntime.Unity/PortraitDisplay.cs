using PortraitRuntime.Core;
using UnityEngine;

namespace PortraitRuntime.Unity;

/// <summary>
/// Render mechanism only: hold the portrait renderer, swap in a content asset, put the original back.
/// Which asset to show, and when, is a content-Mod decision announced through <see cref="PortraitApi"/>.
/// </summary>
public sealed class PortraitDisplay
{
    SpriteRenderer bound;
    Sprite vanilla, applied;
    public SpriteRenderer Renderer => bound;
    public Sprite Vanilla => vanilla;
    /// <summary>True while our asset is still the sprite on the bound renderer. The game resets the
    /// renderer during fades, shadows and pooling, so the content Mod re-asserts when this goes false.</summary>
    public bool IsApplied => bound != null && applied != null && bound.sprite == applied;

    /// <summary>
    /// Track the renderer the next arrival will use. The native sprite is re-read whenever the game
    /// has put a new one on screen (new client, outside-store activation); our own applied sprite is
    /// never mistaken for the native one.
    /// </summary>
    public bool Bind(SpriteRenderer renderer)
    {
        if (renderer == null) { Restore(); return false; }
        if (!ReferenceEquals(renderer, bound)) Restore();
        if (applied == null || !ReferenceEquals(renderer.sprite, applied)) vanilla = renderer.sprite;
        bound = renderer;
        return true;
    }

    public bool Apply(string packId, string path)
    {
        var pack = RuntimeMod.Current?.Catalog.Packs.FirstOrDefault(x => x.Id == packId);
        if (pack == null || string.IsNullOrEmpty(path) || bound == null) return false;
        Apply(pack, path);
        return true;
    }

    void Apply(Pack pack, string path)
    {
        if (bound == null || vanilla == null) return;
        applied = RuntimeMod.Current.Assets.Get(pack, path, vanilla, bound);
        bound.sprite = applied;
        var t = bound.transform; var p = t.position; var s = t.lossyScale; var b = bound.bounds; var c = bound.color;
        RuntimeMod.Event("debug", "portrait.applied", new { pack = pack.Id, path, sprite = applied.name, renderer = bound.name,
            active = bound.gameObject.activeInHierarchy, bound.enabled, position = new[] { p.x,p.y,p.z }, scale = new[] { s.x,s.y,s.z },
            bounds = new[] { b.center.x,b.center.y,b.center.z,b.size.x,b.size.y,b.size.z }, color = new[] { c.r,c.g,c.b,c.a },
            shader = bound.sharedMaterial != null ? bound.sharedMaterial.shader.name : null });
    }

    public void Restore()
    {
        if (bound != null && applied != null && bound.sprite == applied) bound.sprite = vanilla;
        RuntimeMod.Current?.Assets.Release(bound);
        bound = null; vanilla = null; applied = null;
    }
}
