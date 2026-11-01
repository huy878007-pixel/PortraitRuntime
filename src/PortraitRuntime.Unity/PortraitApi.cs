using Newtonsoft.Json;
using PortraitRuntime.Core;
using UnityEngine;

namespace PortraitRuntime.Unity;

/// <summary>
/// Late-bound bridge for separately loaded content Mods.
/// Everything crosses as JSON, BCL delegate types and plain <see cref="object"/> handles, because a
/// dependent Mod that compiles against this assembly cannot load: the game's ModScanner only admits
/// game assemblies plus that Mod's own manifest entries. The dependent side finds this type by
/// reflection at OnEnable.
///
/// The runtime owns the registry and the renderer plumbing; every "replace this with that" decision
/// belongs to the content Mod, which answers through the resolvers below.
/// </summary>
public static class PortraitApi
{
    public static int Version => 2;
    public static string[] Capabilities => new[] { "lifecycle/1", "lookup/1", "assets/1", "apply/1", "status/1" };
    public static int SubscriberCount { get { lock (subscribers) return subscribers.Count; } }
    public static bool SpriteResolverReady => spriteResolver != null;

    // Lifecycle names. A handler receives (name, jsonPayload).
    public const string Enabled = "runtime.enabled";
    public const string Disabled = "runtime.disabled";
    public const string ContentChanged = "content.changed";
    public const string BeforeArrival = "client.before-arrival";
    public const string Arrival = "client.arrival";
    public const string Leaving = "client.leaving";
    public const string Left = "client.left";
    public const string Dialogue = "dialogue.display";
    public const string Scene = "scene.loaded";
    public const string OutsideStore = "client.outside";

    static readonly List<Action<string, string>> subscribers = new();
    static Func<string, string> spriteResolver;

    public static void Subscribe(Action<string, string> handler)
    {
        if (handler == null) return;
        lock (subscribers) if (!subscribers.Contains(handler)) subscribers.Add(handler);
    }
    public static void Unsubscribe(Action<string, string> handler)
    {
        lock (subscribers) subscribers.Remove(handler);
    }

    /// <summary>
    /// The content Mod answers game sprite lookups: given the native sprite name, return
    /// <c>"packId|relative/path"</c> to replace it, or null to leave it native.
    /// Item icons are not routed here; they belong to the pack-declared item runtime.
    /// </summary>
    public static void SetSpriteResolver(Func<string, string> resolver) => spriteResolver = resolver;

    /// <summary>Consult the content Mod. Kept allocation-free: sprite lookups run on the render path.</summary>
    internal static Func<string, string> SpriteResolver => spriteResolver;

    /// <summary>Fan out one lifecycle event. A throwing subscriber cannot break the runtime.</summary>
    public static void Raise(string name, object payload)
    {
        Action<string, string>[] targets;
        lock (subscribers)
        {
            if (subscribers.Count == 0) return;
            targets = subscribers.ToArray();
        }
        var json = payload == null ? "{}" : JsonConvert.SerializeObject(payload);
        foreach (var handler in targets)
        {
            try { handler(name, json); }
            catch (Exception e) { if (RuntimeMod.Current != null) RuntimeMod.Event("error", "api.subscriber.failed", new { name, exception = e.ToString() }); }
        }
    }

    /// <summary>
    /// Resolve a content asset. With <c>sprite</c> it answers a native sprite name (scene specials,
    /// then character variants), which is what makes the walk-in and walk-out frames correct too;
    /// otherwise it resolves the portrait for a client context.
    /// All results are <c>{ok,pack,path,character,via,reason}</c>.
    /// </summary>
    public static string Lookup(string request)
    {
        var query = Parse(request);
        var sprite = Get(query, "sprite");
        var catalog = RuntimeMod.Current?.Catalog;
        return JsonConvert.SerializeObject(sprite != null
            ? SpriteLookup.ResolveSpriteName(catalog, sprite)
            : SpriteLookup.Resolve(catalog, Get(query, "identity"), Get(query, "factory"), Get(query, "spriteKey"), Get(query, "dialogueKey")));
    }

    /// <summary>apply a resolved asset to the bound portrait renderer -> bool</summary>
    public static bool Apply(string request)
    {
        var query = Parse(request);
        return RuntimeMod.Current?.Portrait.Apply(Get(query, "pack"), Get(query, "path")) == true;
    }
    /// <summary>restore the portrait renderer this bridge currently owns.</summary>
    public static void Restore() => RuntimeMod.Current?.Portrait.Restore();

    /// <summary>
    /// Re-announce whoever is on screen. A content Mod that finished binding late (the loader may
    /// load it before this assembly exists) calls this once to catch up instead of waiting for the
    /// next visitor.
    /// </summary>
    public static void Sync()
    {
        var runtime = RuntimeMod.Current;
        if (runtime == null || !runtime.Enabled) return;
        var client = PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint();
        var renderer = StoreUIManager.Instance?.clientGameObject?.image ?? StoreClientMono.Instance?.image;
        if (client != null && runtime.Portrait.Bind(renderer)) Raise(Arrival, RuntimeMod.Context(client, renderer));
    }

    /// <summary>
    /// Apply a resolved asset to an arbitrary renderer the content Mod located itself
    /// (a scene special, an item icon). Returns false when the target is gone.
    /// </summary>
    public static bool ApplyTo(object target, string packId, string path)
    {
        var runtime = RuntimeMod.Current;
        if (runtime == null || target == null || string.IsNullOrEmpty(path)) return false;
        var owner = target as UnityEngine.Object;
        if (owner == null) return false;
        var pack = runtime.Catalog.Packs.FirstOrDefault(x => x.Id == packId);
        if (pack == null) return false;
        var renderer = owner as SpriteRenderer;
        var image = owner as UnityEngine.UI.Image;
        var template = renderer != null ? renderer.sprite : image != null ? image.sprite : null;
        if (template == null) return false;
        try
        {
            var sprite = runtime.Assets.Get(pack, path, template, owner);
            if (renderer != null) renderer.sprite = sprite; else image.sprite = sprite;
            return true;
        }
        catch (Exception e)
        {
            RuntimeMod.Log("Asset " + packId + "/" + path + " unavailable: " + e.Message);
            return false;
        }
    }
    /// <summary>Drop the cache binding the content Mod took on a renderer it applied to.</summary>
    public static void ReleaseTo(object target) => RuntimeMod.Current?.Assets.Release(target as UnityEngine.Object);

    /// <summary>Load an answer of the form <c>"packId|path"</c>; null when it is unusable.</summary>
    internal static Sprite Load(string answer, Sprite template, object owner = null, Vector2? logicalSize = null)
    {
        var runtime = RuntimeMod.Current;
        if (runtime == null || template == null || string.IsNullOrEmpty(answer)) return null;
        var bar = answer.IndexOf('|');
        if (bar <= 0 || bar == answer.Length - 1) return null;
        var pack = runtime.Catalog.Packs.FirstOrDefault(x => x.Id == answer.Substring(0, bar));
        if (pack == null) return null;
        var path = answer.Substring(bar + 1);
        try { return runtime.Assets.Get(pack, path, template, owner, logicalSize); }
        catch (Exception e)
        {
            RuntimeMod.Log("Asset " + pack.Id + "/" + path + " unavailable: " + e.Message);
            return null;
        }
    }

    public static string Status() => JsonConvert.SerializeObject(new
    {
        version = Version, capabilities = Capabilities, subscribers = SubscriberCount,
        spriteResolver = SpriteResolverReady,
        runtime = RuntimeMod.Current == null ? "absent" : RuntimeMod.Current.Enabled ? "enabled" : "disabled",
        packs = RuntimeMod.Current == null ? Array.Empty<string>() : RuntimeMod.Current.Catalog.Packs.Select(x => x.Id).ToArray(),
    });

    static Dictionary<string, string> Parse(string request)
    {
        if (string.IsNullOrWhiteSpace(request)) return new Dictionary<string, string>();
        try { return JsonConvert.DeserializeObject<Dictionary<string, string>>(request) ?? new Dictionary<string, string>(); }
        catch (JsonException) { return new Dictionary<string, string>(); }
    }
    static string Get(Dictionary<string, string> query, string key) =>
        query.TryGetValue(key, out var value) && value != null && value.Length > 0 ? value : null;
}
