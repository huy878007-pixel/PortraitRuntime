using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace PortraitRuntime.Core;

public sealed class PackException : Exception
{
    public PackException(string message) : base(message) { }
}
public static class PackReader
{
    public const int MaxFiles = 10000;
    public const long MaxBytes = 1024L * 1024 * 1024;
    public static readonly HashSet<string> SpecialKeys = new(new[] { "wanted", "wanted_portait1", "wanted_portait2", "wanted_portait3", "wanted_portait4", "wanted_portait5", "wanted_portait6", "wanted_portait7", "wanted_portait8", "wanted_portait9", "drjackson2" }, StringComparer.Ordinal);
    static readonly Regex IdPattern = new("^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant);
    public static string AssetPath(Pack pack, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')) throw new PackException("Invalid asset path: " + relative);
        var root = Path.GetFullPath(pack.Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new PackException("Asset escapes pack: " + relative);
        for (var node = full; node.Length >= root.Length; node = Path.GetDirectoryName(node))
            if ((File.Exists(node) || System.IO.Directory.Exists(node)) && (File.GetAttributes(node) & FileAttributes.ReparsePoint) != 0) throw new PackException("Linked assets are not allowed: " + relative);
        return full;
    }
    public static Pack Read(string root, ISet<string> knownDialogueKeys = null, int gameBuild = 26337366)
    {
        var manifest = Path.Combine(root, "pack.json");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(manifest) & FileAttributes.ReparsePoint) != 0) throw new PackException("Linked pack manifests are not allowed");
        if (new FileInfo(manifest).Length > 8 * 1024 * 1024) throw new PackException("Manifest exceeds 8 MiB");
        var token = JObject.Parse(File.ReadAllText(manifest), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        var pack = token.ToObject<Pack>() ?? throw new PackException("Empty pack");
        pack.Root = Path.GetFullPath(root);
        Validate(pack, knownDialogueKeys, gameBuild);
        return pack;
    }
    public static void Validate(Pack p, ISet<string> knownDialogueKeys = null, int gameBuild = 26337366)
    {
        void Require(bool test, string message) { if (!test) throw new PackException(p.Id + ": " + message); }
        Require(p.Format == "portrait-pack/1", "Unsupported format");
        Require(!string.IsNullOrEmpty(p.Id) && IdPattern.IsMatch(p.Id), "Invalid pack id");
        Require(p.Characters != null && p.Items != null && p.Special != null && p.Actors != null && p.Stories != null && p.Texts != null && p.Dependencies != null, "Collections cannot be null");
        Require(p.Dependencies.All(x => !string.IsNullOrEmpty(x) && IdPattern.IsMatch(x)), "Invalid dependency");
        foreach (var locale in p.Texts)
            Require(!string.IsNullOrWhiteSpace(locale.Key) && locale.Value != null && locale.Value.All(x => !string.IsNullOrWhiteSpace(x.Key) && x.Value != null), "Invalid locale dictionary: " + locale.Key);
        Require(p.Priority >= 0 && p.Priority <= 1000000, "Priority outside 0..1000000");
        string build = p.GameBuild ?? "";
        bool newer = build.StartsWith(">=", StringComparison.Ordinal);
        Require(int.TryParse(newer ? build.Substring(2) : build, out var expected) && (newer ? gameBuild >= expected : gameBuild == expected), "Incompatible gameBuild: " + build);
        int files = 0, directories = 0; long bytes = 0;
        void Walk(string dir, int depth = 0)
        {
            Require(++directories <= 1000 && depth <= 32, "Pack directory quota exceeded");
            Require((File.GetAttributes(dir) & FileAttributes.ReparsePoint) == 0, "Linked pack directories are not allowed");
            foreach (var f in System.IO.Directory.EnumerateFiles(dir))
            {
                Require((File.GetAttributes(f) & FileAttributes.ReparsePoint) == 0, "Linked files are not allowed");
                files++; bytes += new FileInfo(f).Length;
                Require(files <= MaxFiles && bytes <= MaxBytes, "Pack quota exceeded");
            }
            foreach (var sub in System.IO.Directory.EnumerateDirectories(dir)) Walk(sub, depth + 1);
        }
        Walk(p.Root);
        var checkedImages = new Dictionary<string, (int Width, int Height)>(StringComparer.OrdinalIgnoreCase);
        void Image(string path, int w, int h, bool item = false)
        {
            var full = AssetPath(p, path);
            Require(File.Exists(full), "Missing image: " + path);
            if (!checkedImages.TryGetValue(full, out var size)) checkedImages[full] = size = PngVerifier.Read(full);
            int iw = size.Width, ih = size.Height;
            Require(iw > 0 && ih > 0 && iw <= 8192 && ih <= 8192 && (item ? iw % 32 == 0 && ih % 32 == 0 : iw == w && ih == h), "Invalid dimensions: " + path + $" ({iw}x{ih})");
        }
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var factories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in p.Characters)
        {
            Require(c.Value != null && c.Value.Identifiers != null && c.Value.Factories != null && c.Value.Story != null && c.Value.Variants != null, "Invalid character: " + c.Key);
            foreach (var id in c.Value.Identifiers) Require(Catalog.HasIdentity(id) && identities.Add(id), "Invalid or duplicate identity: " + id);
            foreach (var f in c.Value.Factories) Require(!string.IsNullOrWhiteSpace(f) && factories.Add(f), "Invalid or duplicate factory: " + f);
            if (c.Value.Base != null) Image(c.Value.Base, 1152, 844);
            foreach (var v in c.Value.Variants.Values) Image(v, 1152, 844);
            foreach (var s in c.Value.Story)
            {
                Require(knownDialogueKeys == null || knownDialogueKeys.Contains(s.Key), "Unknown original dialogue key: " + s.Key);
                Image(s.Value, 1152, 844);
            }
        }
        foreach (var item in p.Items)
        {
            var d = item.Value;
            Require(d != null && d.Grid != null && d.Grid.Length == 2 && d.Grid.All(x => x > 0 && x <= 64) && d.Tags != null && d.Value >= 0, "Invalid item: " + item.Key);
            Require(!d.Create || item.Key.StartsWith(p.Id + ".", StringComparison.Ordinal), "New item must use pack namespace: " + item.Key);
            if (d.Icon != null) Image(d.Icon, 0, 0, true);
            if (d.Create && d.Icon != null)
            {
                var size = checkedImages[AssetPath(p, d.Icon)];
                Require((long)size.Width * d.Grid[1] == (long)size.Height * d.Grid[0], "New item icon aspect ratio must match its grid: " + item.Key);
            }
        }
        foreach (var s in p.Special)
        {
            Require(SpecialKeys.Contains(s.Key), "Unknown special key: " + s.Key);
            Image(s.Value, s.Key == "wanted" ? 1332 : s.Key == "drjackson2" ? 1920 : 136, s.Key == "wanted" ? 1860 : s.Key == "drjackson2" ? 1080 : 128);
        }
        void When(Condition c)
        {
            Require(c != null && c.StateEquals != null && c.NotEquals != null && c.Items != null && c.MinDay >= 0 && c.MaxDay >= c.MinDay && c.MinCash >= 0 && c.Items.Values.All(n => n >= 0), "Invalid condition");
            Require(c.StateEquals.Keys.Concat(c.NotEquals.Keys).All(k => !k.StartsWith("_", StringComparison.Ordinal)), "Reserved condition state key");
        }
        void Effects(Effect[] actions)
        {
            Require(actions != null && actions.Length <= 128, "Invalid actions");
            foreach (var e in actions)
            {
                Require(e != null && new[] { "set", "increment", "cash", "giveItem", "queueActor", "extension" }.Contains(e.Op), "Unknown effect");
                Require(e.Op == "cash" || !string.IsNullOrWhiteSpace(e.Key), "Missing effect key");
                Require((e.Op != "set" && e.Op != "increment") || !e.Key.StartsWith("_", StringComparison.Ordinal), "Reserved state key");
                Require(e.Op != "giveItem" || e.Amount > 0 && e.Amount <= 100, "Invalid item amount");
                Require(e.Op != "queueActor" || p.Actors.ContainsKey(e.Key), "Unknown queued actor");
            }
        }
        foreach (var story in p.Stories)
        {
            var s = story.Value;
            Require(s != null && s.Nodes != null && s.Nodes.Count <= 1000 && s.Nodes.ContainsKey(s.Start), "Missing story start: " + story.Key);
            foreach (var n in s.Nodes)
            {
                Require(n.Value != null && n.Value.Choices != null && n.Value.Choices.Length <= 20, "Invalid node: " + n.Key);
                Require(n.Value.End ? n.Value.Next == null && n.Value.Choices.Length == 0 : (n.Value.Next != null) != (n.Value.Choices.Length != 0), "Node must end, continue, or offer choices exclusively: " + n.Key);
                Require(n.Value.Next == null || s.Nodes.ContainsKey(n.Value.Next), "Dangling next: " + n.Key);
                if (n.Value.Portrait != null) Image(n.Value.Portrait, 1152, 844);
                Effects(n.Value.Actions);
                foreach (var choice in n.Value.Choices)
                {
                    Require(choice != null && choice.Next != null && s.Nodes.ContainsKey(choice.Next), "Dangling choice: " + n.Key);
                    When(choice.When); Effects(choice.Actions);
                }
            }
        }
        foreach (var actor in p.Actors)
        {
            var d = actor.Value;
            Require(actor.Key.StartsWith(p.Id + ".", StringComparison.Ordinal), "New actor must use pack namespace");
            Require(d != null && d.Story != null && p.Stories.ContainsKey(d.Story) && d.Character != null && p.Characters.ContainsKey(d.Character), "Actor references missing character or story");
            Require(new[] { "DIALOGUE", "SELL", "BUY", "SELLNBUY" }.Contains(d.Intent) && d.SellItems != null && d.BuyItems != null && d.Cash >= 0, "Invalid actor trade settings");
            When(d.When);
        }
    }
    public static Catalog Load(string directory, Action<string> report, ISet<string> dialogueKeys = null)
        => LoadSources(new[] { directory }, report, dialogueKeys);
    public static Catalog LoadSources(IEnumerable<string> directories, Action<string> report, ISet<string> dialogueKeys = null)
    {
        var loaded = new List<Pack>();
        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
        if (!System.IO.Directory.Exists(directory)) { report("Rejected content source: " + directory); continue; }
        foreach (var root in System.IO.Directory.EnumerateDirectories(directory).OrderBy(x => x, StringComparer.Ordinal))
            try { var p = Read(root, dialogueKeys); if (p.Enabled) loaded.Add(p); }
            catch (Exception e) when (e is PackException || e is JsonException || e is IOException || e is ArgumentException || e is UnauthorizedAccessException) { report("Rejected " + root + ": " + e.Message); }
        }
        var duplicates = loaded.GroupBy(x => x.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var id in duplicates) report("Rejected duplicate pack id: " + id);
        loaded.RemoveAll(p => duplicates.Contains(p.Id));
        bool changed;
        do
        {
            changed = false;
            foreach (var p in loaded.ToArray()) if (p.Dependencies.Any(id => !loaded.Any(x => x.Id == id))) { loaded.Remove(p); report("Missing dependency: " + p.Id); changed = true; }
        } while (changed);
        var catalog = new Catalog();
        foreach (var p in loaded.OrderBy(x => x.Priority).ThenBy(x => x.Id, StringComparer.Ordinal))
            try { catalog.Add(p); }
            catch (ArgumentException e) { report("Rejected " + p.Id + ": " + e.Message); }
        foreach (var conflict in catalog.Conflicts) report("Override " + conflict);
        return catalog;
    }
}
