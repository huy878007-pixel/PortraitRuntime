using Newtonsoft.Json;
using PortraitRuntime.Core;

Console.OutputEncoding = System.Text.Encoding.UTF8;
try
{
    if (args.Length < 2) { Console.WriteLine("validate_pack validate <pack目录> [台词key表]\nvalidate_pack catalog <packs目录>\nvalidate_pack simulate <pack目录> <人物ID> <状态JSON路径> [天数] [选项序号]"); return 2; }
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
