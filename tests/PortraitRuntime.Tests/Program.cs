using PortraitRuntime.Core;

int passed = 0, failed = 0;
void Test(string name, Action body)
{
    try { body(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
}
void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }
Pack PortraitPack(string id, int priority, string identity) => new()
{
    Id = id, Priority = priority,
    Characters = new() { ["lun"] = new() { Identifiers = new[] { identity }, Base = "lun.png" } }
};
Test("identity selects Lun", () => { var c = new Catalog(); c.Add(PortraitPack("core", 100, "officerLunVisit")); Check(c.Character("officerLunVisit")?.Id == "lun"); });
Test("shared sprite NPC stays vanilla", () => { var c = new Catalog(); c.Add(PortraitPack("core", 100, "officerLunVisit")); Check(c.Character("sec") == null); });
Test("NO_ID is not an identity", () => { var c = new Catalog(); c.Add(PortraitPack("core", 100, "NO_ID")); Check(c.Character("NO_ID") == null); });
Test("factory resolves identityless named client", () => { var c = new Catalog(); var p = PortraitPack("core", 100, "officerLunVisit"); p.Characters["lun"].Factories = new[] { "StoreClientListStory.CreateLun" }; c.Add(p); Check(c.Character("NO_ID", "StoreClientListStory.CreateLun")?.Id == "lun"); });
Test("priority works regardless of loading order", () => { var c = new Catalog(); c.Add(PortraitPack("local", 300, "officerLunVisit")); c.Add(PortraitPack("core", 100, "officerLunVisit")); Check(c.Character("officerLunVisit")?.Pack.Id == "local"); });
Test("conflict is reported", () => { var c = new Catalog(); c.Add(PortraitPack("core", 100, "officerLunVisit")); c.Add(PortraitPack("community", 200, "officerLunVisit")); Check(c.Conflicts.Count == 1); });
Test("item IDs remain independent when icons shared", () => { var c = new Catalog(); var p = new Pack { Id = "items", Items = new() { ["one"] = new() { Icon = "one.png" } } }; c.Add(p); Check(c.Item("one") != null && c.Item("two") == null); });
Test("condition checks pack-local flag", () => { var h = new TestHost(); h.Set("core/met", "yes"); Check(StoryEngine.Matches(new Pack { Id = "core" }, new Condition { StateEquals = new() { ["met"] = "yes" } }, h)); });
Test("condition combines day cash and inventory", () => { var h = new TestHost { Day = 4 }; h.GiveItem("token", 2); Check(StoryEngine.Matches(new Pack { Id = "core" }, new Condition { MinDay = 3, MaxDay = 5, MinCash = 50, Items = new() { ["token"] = 2 } }, h)); });
Test("condition rejects unavailable item", () => Check(!StoryEngine.Matches(new Pack { Id = "core" }, new Condition { Items = new() { ["token"] = 1 } }, new TestHost())));
Test("state action survives host serialization", () => { var h = new TestHost(); StoryEngine.Apply(new Pack { Id = "core" }, new[] { new Effect { Key = "met", Value = "yes" } }, "a/start", h); var restored = TestHost.Restore(h.Save()); Check(restored.Get("core/met") == "yes"); });
Test("same node action does not duplicate item reward", () => { var h = new TestHost(); var effects = new[] { new Effect { Op = "giveItem", Key = "token", Amount = 1 } }; StoryEngine.Apply(new Pack { Id = "core" }, effects, "a/reward", h); StoryEngine.Apply(new Pack { Id = "core" }, effects, "a/reward", h); Check(h.CountItem("token") == 1); });
Test("different packs do not share variables", () => { var h = new TestHost(); StoryEngine.Apply(new Pack { Id = "one" }, new[] { new Effect { Key = "x", Value = "a" } }, "x", h); Check(h.Get("two/x") == ""); });
string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
Pack ReadPack(string name) => PackReader.Read(Path.Combine(repo, "packs", name));
string Fixture(Pack pack)
{
    foreach (var story in pack.Stories.Values) foreach (var node in story.Nodes.Values)
        if (!node.End && node.Next == null && node.Choices.Length == 0) node.End = true;
    var dir = Path.Combine(repo, "artifacts", "testpacks", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir); pack.Root = dir;
    File.WriteAllText(Path.Combine(dir, "pack.json"), System.Text.Json.JsonSerializer.Serialize(pack));
    return dir;
}
void Reject(Action action) { bool rejected = false; try { action(); } catch (PackException) { rejected = true; } Check(rejected, "invalid input was accepted"); }
Test("core migration retains 17 characters and 669 mappings", () => { var p = ReadPack("core"); Check(p.Characters.Count == 17 && p.Characters.Sum(x => x.Value.Story.Count) == 669); });
Test("new content validates", () => { var p = ReadPack("demo"); Check(p.Actors.Count == 3 && p.Items["demo.signal"].Create); });
Test("pack rejects traversal", () => { var p = new Pack { Id = "test" }; Fixture(p); Reject(() => PackReader.AssetPath(p, "../outside.png")); });
Test("pack rejects absolute paths", () => { var p = new Pack { Id = "test" }; Fixture(p); Reject(() => PackReader.AssetPath(p, "C:/outside.png")); });
Test("pack rejects duplicate identity", () => { var p = PortraitPack("test", 100, "same"); p.Characters["lun"].Base = null; p.Characters["other"] = new() { Identifiers = new[] { "same" } }; var dir = Fixture(p); Reject(() => PackReader.Read(dir)); });
Test("pack rejects default identity", () => { var p = PortraitPack("test", 100, "NO_ID"); p.Characters["lun"].Base = null; var dir = Fixture(p); Reject(() => PackReader.Read(dir)); });
Test("pack rejects dangling story node", () => { var p = new Pack { Id = "test", Stories = new() { ["s"] = new() { Nodes = new() { ["start"] = new() { Next = "missing" } } } } }; var dir = Fixture(p); Reject(() => PackReader.Read(dir)); });
Test("pack rejects unknown action", () => { var p = new Pack { Id = "test", Stories = new() { ["s"] = new() { Nodes = new() { ["start"] = new() { Actions = new[] { new Effect { Op = "execute" } } } } } } }; var dir = Fixture(p); Reject(() => PackReader.Read(dir)); });
Test("pack rejects reserved state writes", () => { var p = new Pack { Id = "test", Stories = new() { ["s"] = new() { Nodes = new() { ["start"] = new() { Actions = new[] { new Effect { Key = "_effect/x", Value = "done" } } } } } } }; var dir = Fixture(p); Reject(() => PackReader.Read(dir)); });
Test("pack rejects incompatible build", () => { var p = new Pack { Id = "test", GameBuild = "1" }; var dir = Fixture(p); Reject(() => PackReader.Read(dir)); });
Test("pack rejects missing image", () => { var p = PortraitPack("test", 100, "test"); var dir = Fixture(p); Reject(() => PackReader.Read(dir)); });
Test("new item requires own namespace", () => { var p = new Pack { Id = "test", Items = new() { ["foreign.token"] = new() { Create = true } } }; var dir = Fixture(p); Reject(() => PackReader.Read(dir)); });
Test("invalid inventory amount rejected", () => { var p = new Pack { Id = "test", Stories = new() { ["s"] = new() { Nodes = new() { ["start"] = new() { Actions = new[] { new Effect { Op = "giveItem", Key = "x", Amount = -1 } } } } } } }; var dir = Fixture(p); Reject(() => PackReader.Read(dir)); });
Test("demo accept save reload and return", () => { var p = ReadPack("demo"); var h = new SimulationHost { Day = 2 }; Simulation.Run(p, "demo.courier", h, _ => 0); Check(h.CountItem("demo.signal") == 1); var restored = SimulationHost.Load(h.Save()); restored.Day = 3; Simulation.Run(p, "demo.return", restored, _ => 0); Check(restored.Cash == 120 && restored.Get("demo/finished") == "yes" && restored.CountItem("demo.signal") == 1); });
Test("demo refusal persists and blocks return", () => { var p = ReadPack("demo"); var h = new SimulationHost { Day = 2 }; Simulation.Run(p, "demo.courier", h, _ => 1); var restored = SimulationHost.Load(h.Save()); Check(restored.Get("demo/accepted") == "no" && !StoryEngine.Matches(p, p.Actors["demo.return"].When, restored)); });
Test("completed one-time actor cannot reward twice", () => { var p = ReadPack("demo"); var h = new SimulationHost { Day = 2 }; Simulation.Run(p, "demo.courier", h, _ => 0); bool refused = false; try { Simulation.Run(p, "demo.courier", h, _ => 0); } catch (InvalidOperationException) { refused = true; } Check(refused && h.CountItem("demo.signal") == 1); });
Test("cash and inventory survive repeated serialization", () => { var h = new SimulationHost(); h.Credit(20); h.GiveItem("token", 2); for (int i = 0; i < 100; i++) h = SimulationHost.Load(h.Save()); Check(h.Cash == 120 && h.CountItem("token") == 2); });
Test("save reload halfway through story resumes stored node", () => { var p = ReadPack("demo"); var h = new SimulationHost { Day = 2 }; h.Set("demo/_active/demo.courier", "yes"); h.Set("demo/_actor/demo.courier", "visited"); h.Set("demo/_progress/demo.courier/first", "accept"); var output = Simulation.Run(p, "demo.courier", SimulationHost.Load(h.Save()), _ => throw new Exception("start replayed")); Check(output.Count == 1); });
Test("same priority deterministic tie", () => { var a = new Catalog(); var b = new Catalog(); a.Add(PortraitPack("a", 100, "x")); a.Add(PortraitPack("z", 100, "x")); b.Add(PortraitPack("z", 100, "x")); b.Add(PortraitPack("a", 100, "x")); Check(a.Character("x").Pack.Id == b.Character("x").Pack.Id); });
Test("disabled pack has no effect", () => { var p = PortraitPack("x", 100, "x"); p.Enabled = false; var c = new Catalog(); c.Add(p); Check(c.Character("x") == null); });
Test("locale fallback uses english", () => { var p = new Pack { Id = "x", Texts = new() { ["en"] = new() { ["line"] = "hello" } } }; var c = new Catalog(); c.Add(p); Check(c.Text("zh", "line", "original") == "hello"); });
Test("missing pack state roundtrip retained", () => { var h = new SimulationHost(); h.Set("missing/foo", "bar"); Check(SimulationHost.Load(h.Save()).Get("missing/foo") == "bar"); });
Test("once key shared across scopes", () => { var h = new SimulationHost(); var e = new[] { new Effect { Op = "cash", Amount = 20, OnceKey = "reward" } }; var p = new Pack { Id = "test" }; StoryEngine.Apply(p, e, "a", h); StoryEngine.Apply(p, e, "b", h); Check(h.Cash == 120); });
Test("state increment is numeric", () => { var h = new SimulationHost(); StoryEngine.Apply(new Pack { Id = "test" }, new[] { new Effect { Op = "increment", Key = "count", Amount = 2 } }, "a", h); Check(h.Get("test/count") == "2"); });
Test("PNG with truncated data is rejected", () => { var p = PortraitPack("test", 100, "x"); var dir = Fixture(p); var image = File.ReadAllBytes(Path.Combine(repo, "packs/demo/courier.png")); File.WriteAllBytes(Path.Combine(dir, "lun.png"), image.Take(24).ToArray()); Reject(() => PackReader.Read(dir)); });
Test("null locale dictionary rejected before catalog admission", () => { var p = new Pack { Id = "bad", Texts = new() { ["en"] = null! } }; var dir = Fixture(p); Reject(() => PackReader.Read(dir)); });
Test("partial reward delivery resumes without duplication", () => { var h = new FailingHost(); var actions = new[] { new Effect { Op = "giveItem", Key = "token", Amount = 3 } }; var p = new Pack { Id = "test" }; try { StoryEngine.Apply(p, actions, "reward", h); } catch (InvalidOperationException) { } Check(h.CountItem("token") == 1); h.Limit = 3; StoryEngine.Apply(p, actions, "reward", h); Check(h.CountItem("token") == 3); });
Test("active story resumes beyond original trigger day", () => { var p = ReadPack("demo"); p.Actors["demo.courier"].When.MaxDay = 2; var h = new SimulationHost { Day = 8 }; h.Set("demo/_active/demo.courier", "yes"); h.Set("demo/_progress/demo.courier/first", "accept"); Simulation.Run(p, "demo.courier", h, _ => 0); Check(h.CountItem("demo.signal") == 1); });
Test("repeating actors get new visit reward scope", () => { var p = ReadPack("demo"); p.Actors["demo.courier"].Once = false; var h = new SimulationHost { Day = 2 }; Simulation.Run(p, "demo.courier", h, _ => 0); Simulation.Run(p, "demo.courier", h, _ => 0); Check(h.CountItem("demo.signal") == 2 && h.Get("demo/_visit/demo.courier") == "2"); });
Test("visual overlay preserves creator registration", () => { var c = new Catalog(); var source = new Pack { Id = "demo", Items = new() { ["demo.x"] = new() { Create = true, Icon = "a.png" } } }; var overlay = new Pack { Id = "overlay", Priority = 200, Items = new() { ["demo.x"] = new() { Icon = "b.png" } } }; c.Add(source); c.Add(overlay); Check(c.ItemCreators["demo.x"].Pack.Id == "demo" && c.ItemIcons["demo.x"].Pack.Id == "overlay"); });
Test("partial portrait overlay retains lower story mappings", () => { var c = new Catalog(); var source = PortraitPack("core", 100, "lun"); source.Characters["lun"].Story["line"] = "story.png"; var overlay = PortraitPack("local", 300, "lun"); c.Add(source); c.Add(overlay); Check(c.CharacterAsset("lun", null, null, "line")?.Value == "story.png" && c.CharacterAsset("lun", null, null)?.Pack.Id == "local"); });
Test("trusted C# effect extension executes once", () => { StoryEngine.RegisterEffect("test.double", (_, e, h) => h.Credit(e.Amount * 2)); try { var h = new SimulationHost(); var effects = new[] { new Effect { Op = "extension", Key = "test.double", Amount = 10 } }; StoryEngine.Apply(new Pack { Id = "test" }, effects, "custom", h); StoryEngine.Apply(new Pack { Id = "test" }, effects, "custom", h); Check(h.Cash == 120); } finally { StoryEngine.UnregisterEffect("test.double"); } });
Test("failed effects block completion until remaining reward delivered", () => { var p = new Pack { Id = "test" }; var h = new FailingHost(); var e = new[] { new Effect { Op = "giveItem", Key = "token", Amount = 3 } }; Check(!StoryEngine.TryApply(p, e, "reward", "actor", h, _ => { }) && StoryEngine.HasPending(p, "actor", h)); h.Limit = 3; Check(StoryEngine.TryApply(p, e, "reward", "actor", h, _ => { }) && !StoryEngine.HasPending(p, "actor", h) && h.CountItem("token") == 3); });
Test("failed choice locks competing branch until retry succeeds", () => { var p = new Pack { Id = "test" }; var h = new FailingHost(); var e = new[] { new Effect { Op = "giveItem", Key = "token", Amount = 3 } }; Check(!StoryEngine.TryApply(p, e, "start/choice/0", "actor", h, _ => { })); Check(StoryEngine.ChoiceAllowed(p, "actor", "start/choice/0", h) && !StoryEngine.ChoiceAllowed(p, "actor", "start/choice/1", h)); h.Limit = 3; Check(StoryEngine.TryApply(p, e, "start/choice/0", "actor", h, _ => { }) && StoryEngine.ChoiceAllowed(p, "actor", "start/choice/1", h)); });
Test("pending selected choice can retry after trigger condition expires", () => { var p = new Pack { Id = "test" }; var h = new SimulationHost { Day = 8 }; var when = new Condition { MaxDay = 2 }; Check(!StoryEngine.ChoiceEligible(p, "actor", "start/choice/0", when, h)); h.Set("test/_pending/actor", "start/choice/0"); Check(StoryEngine.ChoiceEligible(p, "actor", "start/choice/0", when, h) && !StoryEngine.ChoiceEligible(p, "actor", "start/choice/1", when, h)); });
Test("separate content Mod can register and detach its root", () => { var p = new Pack { Id = "addon" }; var path = Fixture(p); ContentSources.Register("test.addon", path); Check(ContentSources.Snapshot.TryGetValue("test.addon", out var actual) && actual == Path.GetFullPath(path)); Check(ContentSources.Unregister("test.addon") && !ContentSources.Snapshot.ContainsKey("test.addon")); });
Test("tour preserves different stages sharing identity", () => { var plan = new DebugTourPlan(); plan.Start(new[] { new DebugCase { Id = "visit1", Identity = "lun" }, new DebugCase { Id = "visit2", Identity = "lun" } }); Check(plan.Next()?.Id == "visit1" && plan.Next()?.Id == "visit2"); });
Test("tour removes only duplicate case IDs", () => { var plan = new DebugTourPlan(); plan.Start(new[] { new DebugCase { Id = "x" }, new DebugCase { Id = "x" } }); Check(plan.Cases.Count == 1); });
Test("tour completes after exactly all entries", () => { var plan = new DebugTourPlan(); plan.Start(new[] { new DebugCase { Id = "x" } }); Check(plan.Active && plan.Next() != null && plan.Next() == null && !plan.Active); });
Test("tour seek supports previous and replay", () => { var plan = new DebugTourPlan(); plan.Start(new[] { new DebugCase { Id = "a" }, new DebugCase { Id = "b" } }); plan.Next(); plan.Next(); plan.Seek(0); Check(plan.Next()?.Id == "a"); });
Test("detailed log records structured fields and session order", () => { var dir = Fixture(new Pack { Id = "logs" }); using (var log = new TraceLog(Path.Combine(dir,"logs"))) { Check(log.Write("info", "client.arrival", new { identity = "lun" })); Check(log.Write("error", "exception", new { stack = "full stack" })); log.Dispose(); var lines = File.ReadAllLines(log.Files[0]); Check(lines.Length == 2 && lines[0].Contains("client.arrival") && lines[1].Contains("full stack") && lines[1].Contains("\"seq\":2")); } });
Test("log rotation keeps earlier files and bounds session size", () => { var dir = Fixture(new Pack { Id = "logs" }); using var log = new TraceLog(Path.Combine(dir,"logs"),512,2); for(int i=0;i<20;i++) log.Write("info","test",new { text = new string('x',250) }); Check(log.Files.Count == 2 && log.Files.All(x=>new FileInfo(x).Length<=512) && log.Dropped > 0); });
Test("non-detailed logging still keeps errors", () => { var dir = Fixture(new Pack { Id = "logs" }); using var log = new TraceLog(Path.Combine(dir,"logs")) { Detailed=false }; Check(!log.Write("debug","verbose") && log.Write("error","failure")); });
Test("oversized log event preserves file limit and later events", () => { var dir=Fixture(new Pack { Id="logs" }); using var log=new TraceLog(Path.Combine(dir,"logs"),512,2); Check(!log.Write("info","large",new {text=new string('x',1024)})); Check(log.Dropped==1 && log.Write("error","small")); Check(log.Files.All(x=>new FileInfo(x).Length<=512)); });
Test("unwritable log directory does not stop runtime", () => { var dir=Fixture(new Pack { Id="logs" }); var blocked=Path.Combine(dir,"occupied"); File.WriteAllText(blocked,"file"); using var log=new TraceLog(blocked); Check(log.Failure != null && !log.Write("error","test")); log.Dispose(); });
Test("diagnostic observer cannot break story effects", () => { Action<string,object> observer = (_,_)=>throw new Exception("observer"); StoryEngine.Diagnostic+=observer; try{var h=new SimulationHost(); StoryEngine.Apply(new Pack{Id="test"},new[]{new Effect{Key="ok",Value="yes"}},"trace",h);Check(h.Get("test/ok")=="yes");} finally{StoryEngine.Diagnostic-=observer;} });
var visiting = System.Text.Json.JsonSerializer.Deserialize<List<DebugCase>>(
    File.ReadAllText(Path.Combine(repo, "catalogs/debug_visits.json")),
    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
DebugCase Visit(string id) => visiting.Single(x => x.Id == id);
Test("unresolved sprite never borrows the first dictionary entry", () =>
{
    var dict = new Dictionary<string, string> { ["mentor"] = "M", ["maleScav1"] = "S" };
    Check(!SpriteResolver.TryPick(null, Array.Empty<string>(), dict.ContainsKey, out var sprite, out var status) && sprite == null && status == "unresolved");
    Check(!SpriteResolver.TryPick("scavElder", new[] { "scavElder" }, dict.ContainsKey, out _, out var missing) && missing == "native-key-missing");
    Check(SpriteResolver.TryPick(null, new[] { "maleScav1" }, dict.ContainsKey, out var picked, out var exact) && picked == "maleScav1" && exact == "exact");
    Check(!SpriteResolver.TryPick("scavElder", Array.Empty<string>(), dict.ContainsKey, out _, out var empty) && empty == "native-key-missing");
    Check(SpriteResolver.TryPick("scavElder", new[] { "maleScav1" }, dict.ContainsKey, out var rescued, out var rescuedStatus) && rescued == "maleScav1" && rescuedStatus == "exact");
});
Test("every debug visitor carries sprite information", () =>
{
    var missing = visiting.Where(x => string.IsNullOrEmpty(x.SpriteKey) && (x.SpriteExpressions?.Length ?? 0) == 0).Select(x => x.Id).ToArray();
    Check(missing.Length == 0, "no sprite info: " + string.Join(", ", missing));
});
Test("wrapper factories inherit sprite and name from their builder", () =>
{
    Check(Visit("visit/scavGenericFoodSeller").SpriteExpressions.Contains("SpriteDict.GetRandomScavSpriteName()"));
    Check(Visit("visit/louBoxDrop").SpriteKey == "wanted5" && Visit("visit/louDemandCollect").SpriteKey == "wanted5" && Visit("visit/louBoxCollect").SpriteKey == "wanted5");
    Check(Visit("visit/wanted6_supply").SpriteKey == "wanted6" && Visit("visit/wanted6_finale").SpriteKey == "wanted6");
    Check(Visit("visit/ulDiscerningShopper").SpriteExpressions.Contains("SpriteDict.GetRandomUpperSpriteName()"));
});
Test("native key defects stay identifiable instead of looking resolved", () =>
{
    Check(Visit("visit/lotteryRep").SpriteKey == null && Visit("visit/lotteryRep").SpriteExpressions.Single().Contains("LocHelper.GetLocalizedClientName"));
    Check(Visit("visit/lowerLevelElder").SpriteExpressions.Single() == "SpriteDict.GetRandomElderSpriteName()");
    Check(Visit("visit/OldScav1").SpriteExpressions.Single() == "SpriteDict.GetRandomElderSpriteName()");
});
Test("no visitor borrows the mentor template", () =>
{
    var mentors = visiting.Where(x => x.SpriteKey == "mentor" && !(x.Factory ?? "").StartsWith("StoreClientListStory.CreateMentor", StringComparison.Ordinal)).Select(x => x.Id).ToArray();
    Check(mentors.Length == 0, "mentor substitution: " + string.Join(", ", mentors));
});
Test("visitor previews keep a real identity instead of NO_ID", () =>
{
    var bad = visiting.Where(x => !Catalog.HasIdentity(x.Identity)).Select(x => x.Id).ToArray();
    Check(bad.Length == 0, "NO_ID: " + string.Join(", ", bad));
});
Test("internal factory labels are replaced by localized names", () =>
{
    foreach (var id in new[] { "visit/louBoxDrop", "visit/louBoxCollect", "visit/louDemandCollect", "visit/wanted6_supply", "visit/wanted6_finale", "visit/scavGenericFoodSeller", "visit/ulDiscerningShopper" })
        Check(Visit(id).Label != Visit(id).RegistrationId, id + " still shows its internal tag");
});
Test("approved previews keep their character and image", () =>
{
    var catalog = new Catalog(); catalog.Add(ReadPack("core"));
    var expected = new Dictionary<string, (string, string)>
    {
        ["visit/wanted1"] = ("bailey", "portraits/base/wanted1.png"),
        ["visit/wanted1_return"] = ("bailey", "portraits/base/wanted1.png"),
        ["visit/louBoxDrop"] = ("lou", "portraits/base/wanted5.png"),
        ["visit/louBoxCollect"] = ("lou", "portraits/base/wanted5.png"),
        ["visit/louDemandCollect"] = ("lou", "portraits/base/wanted5.png"),
        ["visit/wanted6_supply"] = ("li", "portraits/base/wanted6.png"),
        ["visit/wanted6_finale"] = ("li", "portraits/base/wanted6.png"),
    };
    foreach (var pair in expected)
    {
        var c = Visit(pair.Key);
        Check(catalog.Character(c.Identity, c.Factory)?.Id == pair.Value.Item1, pair.Key + " -> " + catalog.Character(c.Identity, c.Factory)?.Id);
        Check(catalog.CharacterAsset(c.Identity, c.Factory, c.SpriteKey)?.Value == pair.Value.Item2, pair.Key + " image -> " + catalog.CharacterAsset(c.Identity, c.Factory, c.SpriteKey)?.Value);
        var resolved = SpriteLookup.Resolve(catalog, c.Identity, c.Factory, c.SpriteKey, null);
        Check(resolved.Ok && resolved.Pack == "core" && resolved.Path == pair.Value.Item2 && resolved.Character == pair.Value.Item1, pair.Key + " bridge -> " + resolved.Path);
    }
});
Test("the content bridge resolves exactly what the preview applies", () =>
{
    var catalog = new Catalog(); catalog.Add(ReadPack("core"));
    foreach (var c in visiting)
    {
        var preview = SpriteLookup.Resolve(catalog, c.Identity, c.Factory, c.SpriteKey, null);
        if (preview.Ok) Check(preview.Path != null && !preview.Path.Contains(".."), c.Id + " escaped the pack");
        else Check(preview.Reason != null, c.Id + " failed without a reason");
    }
    var anonymous = SpriteLookup.Resolve(catalog, "scavGenericFoodSeller", "StoreClientListSupplier.CreateScavFoodSeller", null, null);
    Check(!anonymous.Ok && anonymous.Reason == "no-mapping", "anonymous seller must stay unmapped");
});
Test("anonymous change items are never bound to a named character", () =>
{
    var catalog = new Catalog(); catalog.Add(ReadPack("core"));
    foreach (var id in new[] { "visit/lotteryRep", "visit/lowerLevelElder", "visit/OldScav1", "visit/scavGenericFoodSeller" })
    {
        var c = Visit(id);
        Check(catalog.Character(c.Identity, c.Factory) == null, id + " was bound to " + catalog.Character(c.Identity, c.Factory)?.Id);
    }
});
Test("pack registry answers special and item lookups", () =>
{
    var core = new Catalog(); core.Add(ReadPack("core"));
    var special = SpriteLookup.ResolveAsset(core, "special", "wanted");
    Check(special.Ok && special.Pack == "core" && "special/" + "wanted.png" == "special/" + System.IO.Path.GetFileName(special.Path), "special wanted -> " + special.Path);
    Check(!SpriteLookup.ResolveAsset(core, "special", "mentor").Ok, "a portrait sprite is not a special");
    var demo = new Catalog(); demo.Add(ReadPack("demo"));
    var icon = SpriteLookup.ResolveAsset(demo, "item", "demo.signal");
    Check(icon.Ok && icon.Pack == "demo" && icon.Path != null, "item icon -> " + icon.Path);
    Check(!SpriteLookup.ResolveAsset(demo, "item", "no.such.item").Ok);
    Check(SpriteLookup.ResolveAsset(null, "special", "wanted").Reason == "runtime-absent");
});
Console.WriteLine($"RESULT {passed} passed; {failed} failed");
Environment.ExitCode = failed == 0 ? 0 : 1;

sealed class TestHost : IStoryHost
{
    public int Day { get; set; } = 1;
    public long Cash { get; private set; } = 100;
    public Dictionary<string, string> State { get; set; } = new();
    public Dictionary<string, int> Inventory { get; set; } = new();
    public string Get(string key) => State.TryGetValue(key, out var s) ? s : "";
    public void Set(string key, string value) => State[key] = value;
    public int CountItem(string id) => Inventory.GetValueOrDefault(id);
    public bool GiveItem(string id, int amount) { Inventory[id] = CountItem(id) + amount; return true; }
    public void Credit(int amount) => Cash += amount;
    public bool QueueActor(string id) { Set("queued", id); return true; }
    public string Save() => System.Text.Json.JsonSerializer.Serialize(this);
    public static TestHost Restore(string json) => System.Text.Json.JsonSerializer.Deserialize<TestHost>(json)!;
}
sealed class FailingHost : IStoryHost
{
    readonly SimulationHost host = new();
    public int Limit = 1;
    public int Day => host.Day;
    public long Cash => host.Cash;
    public string Get(string key) => host.Get(key);
    public void Set(string key, string value) => host.Set(key, value);
    public int CountItem(string id) => host.CountItem(id);
    public bool GiveItem(string id, int amount) => CountItem(id) + amount <= Limit && host.GiveItem(id, amount);
    public void Credit(int amount) => host.Credit(amount);
    public bool QueueActor(string id) => host.QueueActor(id);
}
