using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LaunchGiftPortraits.Content;

/// <summary>
/// Answers the core's sprite and item lookups. The core only asks; deciding to replace a native
/// sprite is this package's job, using data registered by its own content packs. Answers are
/// memoised because the core consults the sprite resolver on its render path.
/// </summary>
static class SpritePresenter
{
    static readonly Dictionary<string, string> sprites = new(StringComparer.Ordinal);
    static readonly Dictionary<string, string> items = new(StringComparer.Ordinal);

    public static void Bind()
    {
        CoreBridge.SetSpriteResolver((Func<string, string>)Sprite);
        CoreBridge.SetItemResolver((Func<string, string>)Item);
    }

    public static void Unbind()
    {
        CoreBridge.SetSpriteResolver(null);
        CoreBridge.SetItemResolver(null);
        Clear();
    }

    public static void Clear()
    {
        sprites.Clear();
        items.Clear();
    }

    static string Sprite(string key) => Memo(sprites, "special", key);
    static string Item(string id) => Memo(items, "item", id);

    static string Memo(Dictionary<string, string> cache, string kind, string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (cache.TryGetValue(key, out var cached)) return cached.Length == 0 ? null : cached;
        var answer = Ask(kind, key);
        cache[key] = answer ?? "";
        return answer;
    }

    static string Ask(string kind, string key)
    {
        var reply = CoreBridge.Lookup(JsonConvert.SerializeObject(new { kind, key }));
        if (string.IsNullOrEmpty(reply)) return null;
        try
        {
            var answer = JObject.Parse(reply);
            if (answer.Value<bool?>("ok") != true) return null;
            var pack = Text(answer, "pack");
            var path = Text(answer, "path");
            return pack != null && path != null ? pack + "|" + path : null;
        }
        catch (JsonException) { return null; }
    }

    static string Text(JObject source, string name)
    {
        var token = source?[name];
        return token == null || token.Type == JTokenType.Null ? null : token.ToString();
    }
}
