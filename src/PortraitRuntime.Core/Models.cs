namespace PortraitRuntime.Core;

public sealed class Pack
{
    public string Format { get; set; } = "portrait-pack/1";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string License { get; set; } = "";
    public string GameBuild { get; set; } = "26337366";
    public int Priority { get; set; } = 100;
    public bool Enabled { get; set; } = true;
    public string[] Dependencies { get; set; } = Array.Empty<string>();
    public Dictionary<string, CharacterDef> Characters { get; set; } = new();
    public Dictionary<string, ItemDef> Items { get; set; } = new();
    public Dictionary<string, string> Special { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> Texts { get; set; } = new();
    public Dictionary<string, ActorDef> Actors { get; set; } = new();
    public Dictionary<string, StoryDef> Stories { get; set; } = new();
    [Newtonsoft.Json.JsonIgnore] public string Root { get; set; } = "";
}
public sealed class CharacterDef
{
    public string[] Identifiers { get; set; } = Array.Empty<string>();
    public string[] Factories { get; set; } = Array.Empty<string>();
    public string Base { get; set; }
    public Dictionary<string, string> Variants { get; set; } = new();
    public Dictionary<string, string> Story { get; set; } = new();
}
public sealed class ItemDef
{
    public bool Create { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; }
    public string Atlas { get; set; } = "Items/itematlas";
    public string Sprite { get; set; } = "unknown";
    public int[] Grid { get; set; } = new[] { 1, 1 };
    public long Value { get; set; } = 1;
    public string[] Tags { get; set; } = Array.Empty<string>();
}
public sealed class ActorDef
{
    public string Name { get; set; } = "";
    public string Character { get; set; }
    public string VanillaSprite { get; set; } = "default";
    public string Story { get; set; }
    public string Intent { get; set; } = "DIALOGUE";
    public string[] SellItems { get; set; } = Array.Empty<string>();
    public string[] BuyItems { get; set; } = Array.Empty<string>();
    public int Cash { get; set; } = 100;
    public bool Once { get; set; } = true;
    public Condition When { get; set; } = new();
}
public sealed class StoryDef
{
    public string Start { get; set; } = "start";
    public Dictionary<string, NodeDef> Nodes { get; set; } = new();
}
public sealed class NodeDef
{
    public string Text { get; set; } = "";
    public string Portrait { get; set; }
    public string Next { get; set; }
    public bool End { get; set; }
    public Effect[] Actions { get; set; } = Array.Empty<Effect>();
    public ChoiceDef[] Choices { get; set; } = Array.Empty<ChoiceDef>();
}
public sealed class ChoiceDef
{
    public string Text { get; set; } = "";
    public string Next { get; set; }
    public Condition When { get; set; } = new();
    public Effect[] Actions { get; set; } = Array.Empty<Effect>();
}
public sealed class Condition
{
    public int MinDay { get; set; }
    public int MaxDay { get; set; } = int.MaxValue;
    public long MinCash { get; set; }
    [Newtonsoft.Json.JsonProperty("equals")] public Dictionary<string, string> StateEquals { get; set; } = new();
    public Dictionary<string, string> NotEquals { get; set; } = new();
    public Dictionary<string, int> Items { get; set; } = new();
}
public sealed class Effect
{
    public string Op { get; set; } = "set";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public int Amount { get; set; } = 1;
    public string OnceKey { get; set; }
}
public sealed class Entry<T>
{
    public Pack Pack { get; }
    public string Id { get; }
    public T Value { get; }
    public Entry(Pack pack, string id, T value) { Pack = pack; Id = id; Value = value; }
}
