using Newtonsoft.Json;
using PortraitRuntime.Core;
using UnityEngine;

namespace PortraitRuntime.Unity;

/// <summary>
/// Late-bound bridge for separately loaded content Mods.
/// Everything crosses as JSON and BCL delegate types, because a dependent Mod that compiles against
/// this assembly cannot load: the game's ModScanner only admits game assemblies plus that Mod's own
/// manifest entries. The dependent side finds this type by reflection at OnEnable.
/// </summary>
public static class PortraitApi
{
    public static int Version => 1;
    public static string[] Capabilities => new[] { "lifecycle/1", "lookup/1", "apply/1", "status/1" };
    public static int SubscriberCount { get { lock (subscribers) return subscribers.Count; } }

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

    public static void Subscribe(Action<string, string> handler)
    {
        if (handler == null) return;
        lock (subscribers) if (!subscribers.Contains(handler)) subscribers.Add(handler);
    }
    public static void Unsubscribe(Action<string, string> handler)
    {
        lock (subscribers) subscribers.Remove(handler);
    }
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

    /// <summary>resolve what a content pack would show for a client context -> {ok,pack,path,character,via,reason}</summary>
    public static string Lookup(string request)
    {
        var query = Parse(request);
        return JsonConvert.SerializeObject(SpriteLookup.Resolve(RuntimeMod.Current?.Catalog,
            Get(query, "identity"), Get(query, "factory"), Get(query, "spriteKey"), Get(query, "dialogueKey")));
    }
    /// <summary>apply a resolved content asset to the bound portrait renderer -> bool</summary>
    public static bool Apply(string request)
    {
        var query = Parse(request);
        return RuntimeMod.Current?.Portrait.Apply(Get(query, "pack"), Get(query, "path")) == true;
    }
    /// <summary>restore the renderer this bridge currently owns.</summary>
    public static void Restore() => RuntimeMod.Current?.Portrait.Restore();
    public static string Status() => JsonConvert.SerializeObject(new
    {
        version = Version, capabilities = Capabilities, subscribers = SubscriberCount,
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
