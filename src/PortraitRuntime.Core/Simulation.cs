using Newtonsoft.Json;

namespace PortraitRuntime.Core;

public sealed class SimulationHost : IStoryHost
{
    public int Day { get; set; } = 1;
    public long Cash { get; set; } = 100;
    public Dictionary<string, string> State { get; set; } = new();
    public Dictionary<string, int> Inventory { get; set; } = new();
    public List<string> Queued { get; set; } = new();
    public string Get(string key) => State.TryGetValue(key, out var value) ? value : "";
    public void Set(string key, string value) => State[key] = value;
    public int CountItem(string id) => Inventory.TryGetValue(id, out var value) ? value : 0;
    public bool GiveItem(string id, int amount) { Inventory[id] = checked(CountItem(id) + amount); return true; }
    public void Credit(int amount) => Cash = checked(Cash + amount);
    public bool QueueActor(string id) { if (!Queued.Contains(id)) Queued.Add(id); return true; }
    public string Save() => JsonConvert.SerializeObject(this, Formatting.Indented);
    public static SimulationHost Load(string json) => JsonConvert.DeserializeObject<SimulationHost>(json) ?? throw new InvalidDataException("Invalid simulation state");
}
public static class Simulation
{
    public static List<string> Run(Pack pack, string actorId, SimulationHost host, Func<NodeDef, int> choose)
    {
        if (!pack.Actors.TryGetValue(actorId, out var actor)) throw new ArgumentException("Unknown actor: " + actorId);
        var story = pack.Stories[actor.Story];
        string prefix = pack.Id + "/";
        bool active = host.Get(prefix + "_active/" + actorId) == "yes";
        if (!active && !StoryEngine.Matches(pack, actor.When, host)) throw new InvalidOperationException("Actor conditions not met");
        if (actor.Once && host.Get(prefix + "_actor/" + actorId) == "visited" && host.Get(prefix + "_active/" + actorId) != "yes") throw new InvalidOperationException("Actor already visited");
        int.TryParse(host.Get(prefix + "_visit/" + actorId), out var visit);
        if (!active) visit++;
        host.Set(prefix + "_visit/" + actorId, visit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        host.Set(prefix + "_actor/" + actorId, "visited");
        host.Set(prefix + "_active/" + actorId, "yes");
        string progressKey = prefix + "_progress/" + actorId + "/" + actor.Story;
        var nodeId = active ? host.Get(progressKey) : "";
        if (!story.Nodes.ContainsKey(nodeId)) nodeId = story.Start;
        var output = new List<string>();
        for (int step = 0; step < 1000; step++)
        {
            var node = story.Nodes[nodeId];
            host.Set(progressKey, nodeId);
            string scope = actorId + "/visit/" + visit + "/" + actor.Story + "/" + nodeId;
            StoryEngine.Apply(pack, node.Actions, scope, host);
            output.Add(node.Text);
            if (node.End)
            {
                host.Set(prefix + "_active/" + actorId, "no");
                host.Set(prefix + "_completed/" + actorId + "/" + actor.Story, "yes");
                return output;
            }
            if (node.Choices.Length != 0)
            {
                int index = choose(node);
                if (index < 0 || index >= node.Choices.Length) throw new ArgumentException("Invalid choice");
                var choice = node.Choices[index];
                if (!StoryEngine.Matches(pack, choice.When, host)) throw new InvalidOperationException("Choice conditions not met");
                StoryEngine.Apply(pack, choice.Actions, scope + "/choice/" + index, host);
                nodeId = choice.Next;
            }
            else if (node.Next != null) nodeId = node.Next;
            else return output;
        }
        throw new InvalidOperationException("Story exceeded 1000 simulation steps");
    }
}
