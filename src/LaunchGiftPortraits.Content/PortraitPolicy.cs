using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LaunchGiftPortraits.Content;

/// <summary>
/// Presentation policy owned by this content Mod: which asset to show for the current client and
/// dialogue line, and when to hand the renderer back. The core runtime only announces lifecycle
/// nodes and applies whatever this Mod resolves, so the policy can change without touching it.
/// </summary>
static class PortraitPolicy
{
    static string identity, factory, spriteKey;
    public static string LastPath { get; private set; }
    public static string LastStage { get; private set; } = "";

    public static void OnEvent(string name, string payload)
    {
        switch (name)
        {
            case PortraitApiNames.Arrival:
            case PortraitApiNames.Outside: Arrival(Parse(payload)); break;
            case PortraitApiNames.Dialogue: Dialogue(Parse(payload)); break;
            case PortraitApiNames.Leaving:
                // The exit animation must not keep a story-differential image: fall back to the base.
                Arrival(Parse(payload)?["context"] as JObject);
                break;
            case PortraitApiNames.BeforeArrival:
            case PortraitApiNames.Left: Forget(); break;
        }
    }

    static void Arrival(JObject context)
    {
        if (context == null) return;
        identity = Text(context, "identity");
        factory = Text(context, "factory");
        spriteKey = Text(context, "spriteKey");
        Show(null, "base");
    }

    static void Dialogue(JObject context)
    {
        if (context == null) return;
        var key = Text(context, "key");
        if (key == null) return;                              // untracked line: leave the portrait alone
        if (Text(context, "identity") != identity) Arrival(context);
        Show(key, "story");
    }

    static void Show(string dialogueKey, string stage)
    {
        if (identity == null && factory == null) return;
        var request = JsonConvert.SerializeObject(new { identity, factory, spriteKey, dialogueKey });
        var answer = Parse(CoreBridge.Lookup(request));
        if (answer == null || answer.Value<bool?>("ok") != true) return;
        var pack = Text(answer, "pack");
        var path = Text(answer, "path");
        if (pack == null || path == null) return;
        if (!CoreBridge.Apply(JsonConvert.SerializeObject(new { pack, path }))) return;
        LastPath = path;
        LastStage = stage;
    }

    static void Forget() { identity = null; factory = null; spriteKey = null; LastPath = null; LastStage = ""; }

    static JObject Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JObject.Parse(json); } catch (JsonException) { return null; }
    }
    static string Text(JObject source, string name)
    {
        var token = source?[name];
        return token == null || token.Type == JTokenType.Null ? null : token.ToString();
    }
}

/// <summary>Lifecycle names published by the core API; duplicated here because this Mod cannot reference it.</summary>
static class PortraitApiNames
{
    public const string Arrival = "client.arrival";
    public const string BeforeArrival = "client.before-arrival";
    public const string Leaving = "client.leaving";
    public const string Left = "client.left";
    public const string Dialogue = "dialogue.display";
    public const string Outside = "client.outside";
}
