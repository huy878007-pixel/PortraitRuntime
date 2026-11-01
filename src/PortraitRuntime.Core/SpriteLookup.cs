using Newtonsoft.Json;

namespace PortraitRuntime.Core;

/// <summary>
/// The single rule that decides which content asset represents a client: identity first, then the
/// factory that produced it, then the story line being shown. Content Mods ask through the bridge,
/// so this stays the one place the rule is implemented.
/// </summary>
public static class SpriteLookup
{
    public sealed class Result
    {
        [JsonProperty("ok")] public bool Ok { get; set; }
        [JsonProperty("pack")] public string Pack { get; set; }
        [JsonProperty("path")] public string Path { get; set; }
        [JsonProperty("character")] public string Character { get; set; }
        [JsonProperty("via")] public string Via { get; set; }
        [JsonProperty("reason")] public string Reason { get; set; }
    }

    public static Result Resolve(Catalog catalog, string identity, string factory, string spriteKey, string dialogueKey)
    {
        if (catalog == null) return new Result { Reason = "runtime-absent" };
        var character = catalog.Character(identity, factory);
        var asset = catalog.CharacterAsset(identity, factory, spriteKey, dialogueKey);
        if (asset != null)
            return new Result { Ok = true, Pack = asset.Pack.Id, Path = asset.Value, Character = character?.Id, Via = dialogueKey != null ? "story" : "asset" };
        if (character == null) return new Result { Reason = "no-mapping" };
        var definition = character.Value;
        var path = spriteKey != null && definition.Variants.TryGetValue(spriteKey, out var variant) ? variant : definition.Base;
        return path == null
            ? new Result { Character = character.Id, Reason = "no-image" }
            : new Result { Ok = true, Pack = character.Pack.Id, Path = path, Character = character.Id, Via = "character" };
    }

    /// <summary>
    /// Resolve a scene special the packs declare (wanted poster, wanted portraits, Dr. Jackson).
    /// The registry lives here because packs are data; whether to use it stays a content Mod decision.
    /// Item icons are not routed through this lookup: they belong to the pack-declared item runtime.
    /// </summary>
    public static Result ResolveSpecial(Catalog catalog, string key)
    {
        if (catalog == null) return new Result { Reason = "runtime-absent" };
        if (string.IsNullOrEmpty(key)) return new Result { Reason = "no-mapping" };
        if (catalog.Specials.TryGetValue(key, out var special) && special.Value != null)
            return new Result { Ok = true, Pack = special.Pack.Id, Path = special.Value, Via = "special" };
        return new Result { Reason = "no-mapping" };
    }

    /// <summary>
    /// Resolve a native sprite name to the character that claims it. Only names a character declares
    /// in its own variants table are eligible, so a shared generic sprite can never be dressed up as a
    /// named character; a name claimed by two characters is refused rather than guessed.
    /// The name lookup is what makes the portrait correct the moment the game asks for the sprite,
    /// i.e. while the client is still walking in and out, not only once they reach the counter.
    /// </summary>
    public static Result ResolveVariant(Catalog catalog, string spriteName)
    {
        if (catalog == null) return new Result { Reason = "runtime-absent" };
        if (string.IsNullOrEmpty(spriteName)) return new Result { Reason = "no-mapping" };
        Result found = null;
        var claims = 0;
        foreach (var pack in catalog.Packs)
            foreach (var character in pack.Characters)
            {
                if (!character.Value.Variants.TryGetValue(spriteName, out var path) || path == null) continue;
                claims++;
                if (found == null) found = new Result { Ok = true, Pack = pack.Id, Path = path, Character = character.Key, Via = "variant" };
            }
        return claims == 1 ? found : new Result { Reason = claims > 1 ? "ambiguous" : "no-mapping" };
    }

    /// <summary>The two name spaces a pack can claim: scene specials, then character variants.</summary>
    public static Result ResolveSpriteName(Catalog catalog, string spriteName)
    {
        var special = ResolveSpecial(catalog, spriteName);
        return special.Ok ? special : ResolveVariant(catalog, spriteName);
    }
}
