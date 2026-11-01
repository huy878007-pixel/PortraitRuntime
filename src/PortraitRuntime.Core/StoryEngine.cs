namespace PortraitRuntime.Core;

public interface IStoryHost
{
    int Day { get; }
    long Cash { get; }
    string Get(string key);
    void Set(string key, string value);
    int CountItem(string id);
    bool GiveItem(string id, int amount);
    void Credit(int amount);
    bool QueueActor(string id);
}
public static class StoryEngine
{
    static readonly Dictionary<string, Action<Pack, Effect, IStoryHost>> extensions = new(StringComparer.Ordinal);
    public static void RegisterEffect(string id, Action<Pack, Effect, IStoryHost> handler)
    {
        if (string.IsNullOrWhiteSpace(id) || handler == null || extensions.ContainsKey(id)) throw new ArgumentException("Invalid or duplicate extension id: " + id);
        extensions.Add(id, handler);
    }
    public static bool UnregisterEffect(string id) => extensions.Remove(id);
    public static bool HasPending(Pack pack, string actorId, IStoryHost host) => !string.IsNullOrEmpty(host.Get(pack.Id + "/_pending/" + actorId));
    public static bool ChoiceAllowed(Pack pack, string actorId, string choiceScope, IStoryHost host)
    {
        var pending = host.Get(pack.Id + "/_pending/" + actorId);
        return string.IsNullOrEmpty(pending) || pending == choiceScope;
    }
    public static bool ChoiceEligible(Pack pack, string actorId, string choiceScope, Condition when, IStoryHost host)
    {
        var pending = host.Get(pack.Id + "/_pending/" + actorId);
        return string.IsNullOrEmpty(pending) ? Matches(pack, when, host) : pending == choiceScope;
    }
    public static bool TryApply(Pack pack, Effect[] effects, string scope, string actorId, IStoryHost host, Action<string> report)
    {
        var key = pack.Id + "/_pending/" + actorId;
        try
        {
            Apply(pack, effects, scope, host);
            if (host.Get(key) == scope) host.Set(key, "");
            return true;
        }
        catch (Exception e)
        {
            host.Set(key, scope);
            report("Pending effects for " + actorId + ": " + e.Message);
            return false;
        }
    }
    public static bool Matches(Pack pack, Condition when, IStoryHost host)
    {
        if (when == null) return true;
        return host.Day >= when.MinDay && host.Day <= when.MaxDay && host.Cash >= when.MinCash
            && when.StateEquals.All(x => host.Get(pack.Id + "/" + x.Key) == x.Value)
            && when.NotEquals.All(x => host.Get(pack.Id + "/" + x.Key) != x.Value)
            && when.Items.All(x => host.CountItem(x.Key) >= x.Value);
    }
    public static void Apply(Pack pack, Effect[] actions, string scope, IStoryHost host)
    {
        for (int i = 0; i < actions.Length; i++)
        {
            var action = actions[i];
            var marker = pack.Id + "/_effect/" + (action.OnceKey ?? scope + "/" + i);
            if (host.Get(marker) == "done") continue;
            switch (action.Op)
            {
                case "set": host.Set(pack.Id + "/" + action.Key, action.Value); break;
                case "increment":
                    int.TryParse(host.Get(pack.Id + "/" + action.Key), out var number);
                    host.Set(pack.Id + "/" + action.Key, checked(number + action.Amount).ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
                case "cash":
                    if (host.Cash + action.Amount < 0) throw new InvalidOperationException("Insufficient cash");
                    host.Credit(action.Amount); break;
                case "giveItem":
                    int.TryParse(host.Get(marker + "/delivered"), out var delivered);
                    while (delivered < action.Amount)
                    {
                        if (!host.GiveItem(action.Key, 1)) throw new InvalidOperationException("Cannot give item: " + action.Key);
                        delivered++;
                        host.Set(marker + "/delivered", delivered.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                    break;
                case "queueActor":
                    if (!host.QueueActor(action.Key)) throw new InvalidOperationException("Cannot queue actor: " + action.Key);
                    break;
                case "extension":
                    if (!extensions.TryGetValue(action.Key, out var handler)) throw new InvalidOperationException("Required C# extension not loaded: " + action.Key);
                    handler(pack, action, host); break;
                default: throw new InvalidOperationException("Unknown effect: " + action.Op);
            }
            host.Set(marker, "done");
        }
    }
}
