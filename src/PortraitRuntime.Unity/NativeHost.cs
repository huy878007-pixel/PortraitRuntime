using PortraitRuntime.Core;

namespace PortraitRuntime.Unity;

public sealed class NativeHost : IStoryHost
{
    public int Day => StoreStation.Instance?.dayCounter ?? 0;
    public long Cash => PlayerStore.Instance?.playerCash ?? 0;
    public string Get(string key) => PlayerStore.Instance == null ? "" : ModHelper.GetModData(RuntimeMod.ModId, key, "");
    public void Set(string key, string value)
    {
        if (PlayerStore.Instance == null) throw new InvalidOperationException("No loaded run");
        ModHelper.SetModData(RuntimeMod.ModId, key, value);
        RuntimeMod.Event("debug", "state.write", new { key, value });
    }
    public int CountItem(string id)
    {
        return PlayerStore.Instance == null || EmporiumEntry.Instance == null ? 0 : PlayerStore.Instance.FindAllItem(true).Where(x => x.identifier == id).Sum(x => x.unitCount);
    }
    public bool GiveItem(string id, int amount)
    {
        if (EmporiumEntry.Instance?.invElement == null) return false;
        for (int i = 0; i < amount; i++)
        {
            var item = DirectoryMaster.Item(id);
            if (item == null) return false;
            GeneralHelper.SetItemOwned(item, true);
            GraphUtils.EmporiumTryAdd(item);
            if (item.parentInventory == null) { RuntimeMod.Log("Unplaced reward: " + id); return false; }
        }
        return true;
    }
    public void Credit(int amount) => PlayerStore.Instance.ModCash(amount, false);
    public bool QueueActor(string id)
    {
        var r = RuntimeMod.Current;
        var manager = PlayerStore.Instance?.storeClientManager;
        if (r == null || manager?.clientStack == null || !r.Catalog.Actors.TryGetValue(id, out var entry)) return false;
        if (manager.clientStack.Any(x => x.identifier == id) || PlayerStore.Instance.currentClientInstance?.GetClientBlueprint()?.identifier == id) return true;
        manager.AddClient(ActorBuilder.Create(r, entry));
        RuntimeMod.Log("Queued " + id);
        return true;
    }
    public void CheckDependencies()
    {
        if (PlayerStore.Instance == null) return;
        foreach (var key in ModHelper.GetModDataKeys(RuntimeMod.ModId))
        {
            var packId = key.Split('/')[0];
            if (!RuntimeMod.Current.Catalog.Packs.Any(x => x.Id == packId)) RuntimeMod.Log("Saved state retained for missing pack: " + packId);
        }
    }
}
public static class ActorBuilder
{
    public static StoreClient Create(RuntimeMod r, Entry<ActorDef> entry)
    {
        var d = entry.Value;
        var client = new StoreClient { identifier = entry.Id, displayName = d.Name, spriteName = d.VanillaSprite,
            clientIntent = (StoreClient.ClientIntent)Enum.Parse(typeof(StoreClient.ClientIntent), d.Intent), clientCash = d.Cash };
        client.SetBudget(d.Cash);
        client.sourceModId = RuntimeMod.ModId;
        client.AddBasicDialog();
        client.clientBuyingIdList.AddRange(d.BuyItems);
        var story = r.Catalog.Story(entry.Pack, d.Story) ?? throw new InvalidOperationException("Story missing: " + d.Story);
        var progressKey = entry.Pack.Id + "/_progress/" + entry.Id + "/" + d.Story;
        var actorPrefix = entry.Pack.Id + "/";
        bool resuming = r.Host.Get(actorPrefix + "_active/" + entry.Id) == "yes";
        int.TryParse(r.Host.Get(actorPrefix + "_visit/" + entry.Id), out var visit);
        if (!resuming) visit++;
        var current = resuming ? r.Host.Get(progressKey) : "";
        string start = story.Value.Nodes.ContainsKey(current) ? current : story.Value.Start;
        var nodes = story.Value.Nodes.ToDictionary(x => x.Key, _ => new Dialogue(), StringComparer.Ordinal);
        Dialogue Failure(string nodeId)
        {
            r.Host.Set(progressKey, nodeId);
            r.Host.Set(actorPrefix + "_active/" + entry.Id, "yes");
            return new Dialogue().SetText("CLIENT", "剧情结果尚未执行完成（可能是库存空间不足或所需扩展未加载）。状态已保留。请整理空间后重新读档或等待下一次来访重试，已发放的奖励不会重复。")
                .SetEndAction(() => PlayerStore.Instance.DismissCurrentClient(false));
        }
        foreach (var pair in story.Value.Nodes)
        {
            string nodeId = pair.Key;
            var def = pair.Value;
            var node = nodes[nodeId];
            node.SetText("CLIENT", def.Text);
            r.DialogueTags.Add(node, new RuntimeMod.DialogueTag { Key = entry.Pack.Id + "/" + d.Story + "/" + nodeId, Pack = entry.Pack, Portrait = def.Portrait });
            node.startAction = () => RuntimeMod.Guard("story " + nodeId, () =>
            {
                r.Host.Set(progressKey, nodeId);
                r.Host.Set(entry.Pack.Id + "/_active/" + entry.Id, "yes");
                if (!StoryEngine.TryApply(entry.Pack, def.Actions, entry.Id + "/visit/" + visit + "/" + d.Story + "/" + nodeId, entry.Id, r.Host, RuntimeMod.Log))
                {
                    var failure = Failure(nodeId);
                    node.SetText("CLIENT", failure.plainText); node.choices.Clear(); node.SetNextDialogue(null); node.SetEndAction(failure.endAction);
                }
            });
            if (def.Next != null) node.SetNextDialogue(nodes[def.Next]);
            for (int i = 0; i < def.Choices.Length; i++)
            {
                var choice = def.Choices[i]; int index = i;
                string choiceScope = entry.Id + "/visit/" + visit + "/" + d.Story + "/" + nodeId + "/choice/" + index;
                var edge = ChoiceEdge(choice.Text, nodes[choice.Next]);
                node.AddChoice(ItemDirectory.CreateEmptyItem(), choice.Text, "", edge,
                    () => RuntimeMod.Guard("choice", () =>
                    {
                        if (!StoryEngine.ChoiceAllowed(entry.Pack, entry.Id, choiceScope, r.Host) || !StoryEngine.TryApply(entry.Pack, choice.Actions, choiceScope, entry.Id, r.Host, RuntimeMod.Log)) edge.SetNextDialogue(Failure(nodeId));
                    }),
                    () => StoryEngine.ChoiceEligible(entry.Pack, entry.Id, choiceScope, choice.When, r.Host));
            }
            if (def.End) node.SetEndAction(() => RuntimeMod.Guard("story end", () =>
            {
                if (StoryEngine.HasPending(entry.Pack, entry.Id, r.Host)) { PlayerStore.Instance.DismissCurrentClient(false); return; }
                if (d.Intent == "DIALOGUE") r.Host.Set(entry.Pack.Id + "/_active/" + entry.Id, "no");
                r.Host.Set(entry.Pack.Id + "/_completed/" + entry.Id + "/" + d.Story, "yes");
                if (d.Intent == "DIALOGUE") PlayerStore.Instance.DismissCurrentClient(false);
            }));
        }
        client.mainDialogue = nodes[start];
        client.OnArrival += () => RuntimeMod.Guard("actor arrival", () =>
        {
            r.Host.Set(entry.Pack.Id + "/_actor/" + entry.Id, "visited");
            r.Host.Set(actorPrefix + "_visit/" + entry.Id, visit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            r.Host.Set(actorPrefix + "_active/" + entry.Id, "yes");
            for (int i = 0; i < d.SellItems.Length; i++)
            {
                string stock = actorPrefix + "_stock/" + entry.Id + "/" + visit + "/" + i;
                string tag = "PR_STOCK/" + stock;
                if (r.Host.Get(stock) == "sold" || PlayerStore.Instance.FindAllItem(false).Any(x => x.IsTag(tag))) continue;
                var item = DirectoryMaster.Item(d.SellItems[i]);
                item.EnableTag(tag);
                if (!PlayerStore.Instance.TryAddDirectSellingItemToTable(item)) throw new InvalidOperationException("Cannot place stock: " + d.SellItems[i]);
                r.Host.Set(stock, "offered");
            }
        });
        if (d.Intent != "DIALOGUE") client.OnDismiss += () => { if (!StoryEngine.HasPending(entry.Pack, entry.Id, r.Host)) r.Host.Set(actorPrefix + "_active/" + entry.Id, "no"); };
        client.CompleteClientCreation();
        return client;
    }
    public static Dialogue ChoiceEdge(string text, Dialogue target) => new Dialogue().SetText("PLAYER", text).SetDummy().SetNextDialogue(target);
}
