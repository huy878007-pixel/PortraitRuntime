namespace PortraitRuntime.Core;

public sealed class Catalog
{
    public List<string> Conflicts { get; } = new();
    readonly Dictionary<string, Entry<CharacterDef>> identities = new(StringComparer.Ordinal);
    readonly Dictionary<string, Entry<CharacterDef>> factories = new(StringComparer.Ordinal);
    public Dictionary<string, Entry<CharacterDef>> Characters { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Entry<ItemDef>> Items { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Entry<ItemDef>> ItemCreators { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Entry<ItemDef>> ItemIcons { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Entry<ActorDef>> Actors { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Entry<StoryDef>> Stories { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Entry<string>> Specials { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Entry<string>> Texts { get; } = new(StringComparer.Ordinal);
    public List<Pack> Packs { get; } = new();
    public static bool HasIdentity(string id) => !string.IsNullOrWhiteSpace(id) && id != "NO_ID";
    void Put<T>(Dictionary<string, Entry<T>> table, string key, Entry<T> entry)
    {
        if (table.TryGetValue(key, out var prior))
        {
            Conflicts.Add($"{key}: {prior.Pack.Id} / {entry.Pack.Id}");
            if (prior.Pack.Priority > entry.Pack.Priority || (prior.Pack.Priority == entry.Pack.Priority && StringComparer.Ordinal.Compare(prior.Pack.Id, entry.Pack.Id) > 0)) return;
        }
        table[key] = entry;
    }
    public void Add(Pack pack)
    {
        if (!pack.Enabled) return;
        if (Packs.Any(x => x.Id == pack.Id)) throw new ArgumentException("Duplicate pack id: " + pack.Id);
        Packs.Add(pack);
        foreach (var pair in pack.Characters)
        {
            var entry = new Entry<CharacterDef>(pack, pair.Key, pair.Value);
            Put(Characters, pack.Id + "/" + pair.Key, entry);
            foreach (var id in pair.Value.Identifiers) if (HasIdentity(id)) Put(identities, id, entry);
            foreach (var factory in pair.Value.Factories) Put(factories, factory, entry);
        }
        foreach (var p in pack.Items)
        {
            var entry = new Entry<ItemDef>(pack, p.Key, p.Value);
            Put(Items, p.Key, entry);
            if (p.Value.Create) Put(ItemCreators, p.Key, entry);
            if (p.Value.Icon != null) Put(ItemIcons, p.Key, entry);
        }
        foreach (var p in pack.Actors) Put(Actors, p.Key, new(pack, p.Key, p.Value));
        foreach (var p in pack.Stories) Put(Stories, pack.Id + "/" + p.Key, new(pack, p.Key, p.Value));
        foreach (var p in pack.Special) Put(Specials, p.Key, new(pack, p.Key, p.Value));
        foreach (var lang in pack.Texts)
            foreach (var p in lang.Value) Put(Texts, lang.Key + "/" + p.Key, new(pack, p.Key, p.Value));
    }
    public Entry<CharacterDef> Character(string identity, string factory = null)
    {
        if (HasIdentity(identity) && identities.TryGetValue(identity, out var entry)) return entry;
        return factory != null && factories.TryGetValue(factory, out entry) ? entry : null;
    }
    public Entry<ItemDef> Item(string id) => id != null && Items.TryGetValue(id, out var e) ? e : null;
    public Entry<string> CharacterAsset(string identity, string factory, string spriteKey, string dialogueKey = null)
    {
        var all = Packs.SelectMany(p => p.Characters.Select(c => new Entry<CharacterDef>(p, c.Key, c.Value)));
        var candidates = HasIdentity(identity) ? all.Where(e => e.Value.Identifiers.Contains(identity)).ToArray() : Array.Empty<Entry<CharacterDef>>();
        if (candidates.Length == 0 && factory != null) candidates = all.Where(e => e.Value.Factories.Contains(factory)).ToArray();
        foreach (var e in candidates.OrderByDescending(x => x.Pack.Priority).ThenByDescending(x => x.Pack.Id, StringComparer.Ordinal))
        {
            string path;
            if (dialogueKey != null)
            { if (!e.Value.Story.TryGetValue(dialogueKey, out path)) continue; }
            else path = spriteKey != null && e.Value.Variants.TryGetValue(spriteKey, out var variant) ? variant : e.Value.Base;
            if (path != null) return new Entry<string>(e.Pack, e.Id, path);
        }
        return null;
    }
    public string Text(string locale, string key, string fallback)
    {
        if (Texts.TryGetValue(locale + "/" + key, out var e) || Texts.TryGetValue("en/" + key, out e)) return e.Value;
        return fallback;
    }
    public Entry<StoryDef> Story(Pack pack, string id) => id != null && Stories.TryGetValue(pack.Id + "/" + id, out var e) ? e : null;
}
