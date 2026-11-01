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
    /// (a variant key, or the file stem of its own artwork) are eligible, so a shared generic sprite
    /// can never be dressed up as a named character; a name claimed by two characters is refused
    /// rather than guessed. This is the lookup that makes the walk-in and walk-out frames correct,
    /// because the game asks for the sprite by asset name long before the client reaches the counter.
    /// </summary>
    public static Result ResolveVariant(Catalog catalog, string spriteName)
    {
        if (catalog == null) return new Result { Reason = "runtime-absent" };
        if (string.IsNullOrEmpty(spriteName)) return new Result { Reason = "no-mapping" };
        var claims = catalog.SpriteClaims(spriteName);
        if (claims == null || claims.Count == 0) return new Result { Reason = "no-mapping" };
        if (claims.Select(x => x.Pack.Id + "/" + x.Id).Distinct(StringComparer.Ordinal).Count() > 1)
            return new Result { Reason = "ambiguous" };
        var winner = claims[0];
        return new Result { Ok = true, Pack = winner.Pack.Id, Path = winner.Value, Character = winner.Id, Via = "variant" };
    }

    /// <summary>The two name spaces a pack can claim: scene specials, then character variants.</summary>
    public static Result ResolveSpriteName(Catalog catalog, string spriteName)
    {
        var special = ResolveSpecial(catalog, spriteName);
        return special.Ok ? special : ResolveVariant(catalog, spriteName);
    }
}
