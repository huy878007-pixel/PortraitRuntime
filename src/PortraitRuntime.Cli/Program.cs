using Newtonsoft.Json;
using PortraitRuntime.Core;

Console.OutputEncoding = System.Text.Encoding.UTF8;
try
{
    if (args.Length < 2) { Console.WriteLine("validate_pack validate <pack目录> [台词key表]\nvalidate_pack catalog <packs目录>\nvalidate_pack audit <catalogs目录> <输出JSON> <packs目录>...\nvalidate_pack simulate <pack目录> <人物ID> <状态JSON路径> [天数] [选项序号]"); return 2; }
    if (args[0] == "validate")
    {
        var keys = args.Length > 2 ? JsonConvert.DeserializeObject<HashSet<string>>(File.ReadAllText(args[2])) : null;
        var p = PackReader.Read(args[1], keys);
        Console.WriteLine(JsonConvert.SerializeObject(new { valid = true, p.Id, characters = p.Characters.Count, items = p.Items.Count, actors = p.Actors.Count, stories = p.Stories.Count }, Formatting.Indented));
    }
    else if (args[0] == "catalog")
    {
        var errors = new List<string>();
        var c = PackReader.Load(args[1], errors.Add);
        Console.WriteLine(JsonConvert.SerializeObject(new { packs = c.Packs.Select(x => x.Id), conflicts = c.Conflicts, messages = errors }, Formatting.Indented));
        if (errors.Any(x => x.StartsWith("Rejected", StringComparison.Ordinal) || x.StartsWith("Missing", StringComparison.Ordinal))) return 1;
    }
    else if (args[0] == "audit" && args.Length >= 4)
    {
        // Answers "what will the player actually see for each visitor" without launching the game.
        // Several pack roots are accepted because the runtime and the beauty content live apart.
        var errors = new List<string>();
        var catalog = PackReader.LoadSources(args.Skip(3).ToArray(), errors.Add);
        var visits = JsonConvert.DeserializeObject<List<DebugCase>>(File.ReadAllText(Path.Combine(args[1], "debug_visits.json"))) ?? new();
        var rows = visits.Select(c =>
        {
            var r = SpriteLookup.Resolve(catalog, c.Identity, c.Factory, c.SpriteKey, null);
            return new { id = c.Id, label = c.Label, factory = c.Factory, identity = c.Identity, spriteKey = c.SpriteKey,
                ok = r.Ok, pack = r.Pack, path = r.Path, character = r.Character, via = r.Via, reason = r.Reason };
        }).ToList();
        var characters = new List<object>();
        foreach (var pack in catalog.Packs)
            foreach (var pair in pack.Characters)
            {
                var variants = pair.Value.Variants.Select(v =>
                {
                    var r = SpriteLookup.ResolveSpriteName(catalog, v.Key);
                    var byStem = SpriteLookup.ResolveSpriteName(catalog, Path.GetFileNameWithoutExtension(v.Value));
                    return new { key = v.Key, path = v.Value, keyHit = r.Ok && r.Character == pair.Key, stemHit = byStem.Ok && byStem.Character == pair.Key };
                }).ToList();
                var baseStem = pair.Value.Base == null ? null : Path.GetFileNameWithoutExtension(pair.Value.Base);
                var baseHit = baseStem == null ? false : SpriteLookup.ResolveSpriteName(catalog, baseStem) is { Ok: true } b && b.Character == pair.Key;
                characters.Add(new { pack = pack.Id, id = pair.Key, @base = pair.Value.Base, baseStem, baseHit, variants });
            }
        var stories = new List<object>();
        foreach (var pack in catalog.Packs)
            foreach (var pair in pack.Characters)
                foreach (var line in pair.Value.Story)
                {
                    var full = PackReader.AssetPath(pack, line.Value);
                    int width = 0, height = 0; string problem = null;
                    if (!File.Exists(full)) problem = "missing";
                    else
                    {
                        try { (width, height) = PngVerifier.Read(full); }
                        catch (Exception e) { problem = e.Message; }
                    }
                    if (problem != null) stories.Add(new { pack = pack.Id, character = pair.Key, key = line.Key, path = line.Value, problem });
                }
        var report = new
        {
            generated = DateTime.UtcNow.ToString("O"), packs = catalog.Packs.Select(x => x.Id).ToArray(), conflicts = catalog.Conflicts,
            summary = new
            {
                visits = rows.Count, mapped = rows.Count(x => x.ok), unmapped = rows.Count(x => !x.ok),
                characters = characters.Count, storyLines = catalog.Packs.Sum(p => p.Characters.Sum(c => c.Value.Story.Count)),
                brokenStoryImages = stories.Count, messages = errors.Count
            },
            visits = rows, characters, storyProblems = stories,
        };
        var target = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, JsonConvert.SerializeObject(report, Formatting.Indented));
        Console.WriteLine(JsonConvert.SerializeObject(report.summary, Formatting.Indented));
        return stories.Count == 0 ? 0 : 1;
    }
    else if (args[0] == "simulate" && args.Length >= 4)
    {
        var pack = PackReader.Read(args[1]);
        var host = File.Exists(args[3]) ? SimulationHost.Load(File.ReadAllText(args[3])) : new SimulationHost();
        if (args.Length > 4) host.Day = int.Parse(args[4]);
        int choice = args.Length > 5 ? int.Parse(args[5]) : 0;
        var lines = Simulation.Run(pack, args[2], host, _ => choice);
        var full = Path.GetFullPath(args[3]); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, host.Save());
        Console.WriteLine(JsonConvert.SerializeObject(new { simulated = true, gameRuntime = false, lines, host.Day, host.Cash, host.Inventory }, Formatting.Indented));
    }
    else throw new ArgumentException("Unknown command or missing arguments");
    return 0;
}
catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
