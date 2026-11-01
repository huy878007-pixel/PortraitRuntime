using PortraitRuntime.Core;
using UnityEngine;

namespace PortraitRuntime.Unity;

public sealed class PortraitDisplay
{
    SpriteRenderer bound;
    Sprite vanilla, applied;
    StoreClient client;
    Entry<CharacterDef> definition;
    string factory;
    public SpriteRenderer Renderer => bound;
    public void Arrival(SpriteRenderer preferred = null)
    {
        var r = RuntimeMod.Current;
        var now = PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint();
        if (now == null || r == null) return;
        if (ReferenceEquals(now, client) && bound != null) return;
        if (!ReferenceEquals(now, client))
        {
            Restore();
            client = now; bound = preferred != null ? preferred : StoreClientMono.Instance?.image;
            if (bound == null) return;
            vanilla = bound.sprite;
            factory = r.Origins.TryGetValue(now, out var tag) ? tag.Factory : null;
            definition = r.Catalog.Character(now.identifier, factory);
            if (Catalog.HasIdentity(now.identifier) && r.Catalog.Actors.TryGetValue(now.identifier, out var actor)) r.Catalog.Characters.TryGetValue(actor.Pack.Id + "/" + actor.Value.Character, out definition);
            var test = r.Debugger.CurrentCase;
            if (r.Debugger.IsSynthetic(now) && test?.PackId != null) r.Catalog.Characters.TryGetValue(test.PackId + "/" + test.CharacterId, out definition);
            RuntimeMod.Event("debug", "portrait.resolve", new { now.identifier, factory, now.spriteName, character = definition?.Id, pack = definition?.Pack.Id, renderer = bound.name, original = vanilla?.name });
        }
        if (definition == null) return;
        var asset = r.Catalog.CharacterAsset(now.identifier, factory, now.spriteName);
        if (asset != null) { Apply(asset.Pack, asset.Value); return; }
        var d = definition.Value;
        string path = now.spriteName != null && d.Variants.TryGetValue(now.spriteName, out var variant) ? variant : d.Base;
        if (path != null) Apply(definition.Pack, path);
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
    public void Dialogue(Dialogue dialogue)
    {
        var r = RuntimeMod.Current;
        if (r == null) return;
        if (client == null) Arrival();
        if (!ReferenceEquals(client, PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint())) return;
        if (!r.DialogueTags.TryGetValue(dialogue, out var tag)) return;
        if (tag.Pack != null && tag.Portrait != null) Apply(tag.Pack, tag.Portrait);
        else if (definition != null && tag.Key != null)
        {
            var asset = r.Catalog.CharacterAsset(client.identifier, factory, client.spriteName, tag.Key);
            if (asset != null) Apply(asset.Pack, asset.Value);
        }
    }
    public void Restore()
    {
        if (bound != null && applied != null && bound.sprite == applied) bound.sprite = vanilla;
        RuntimeMod.Current?.Assets.Release(bound);
        bound = null; vanilla = null; applied = null; client = null; definition = null;
    }
}
