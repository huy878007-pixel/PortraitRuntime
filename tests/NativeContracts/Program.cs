using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Xml.Linq;

var managed = Environment.GetEnvironmentVariable("PORTRAIT_GAME_MANAGED") ?? @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var file = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(file) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file) : null;
};
return Contract.Run(args, managed);

static class Contract
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(string[] args, string managed)
    {
        var checks = new List<object>(); bool ok = true;
        void Method(Type type, string name, Type[] parameters = null)
        {
            var method = parameters == null ? HarmonyLib.AccessTools.Method(type, name) : HarmonyLib.AccessTools.Method(type, name, parameters);
            checks.Add(new { target = type.Name + "." + name, found = method != null }); ok &= method != null;
        }
        Method(typeof(PlayerStore), "GetNextClient"); Method(typeof(StoreUIManager), "OnNextClientArrived");
        Method(typeof(StoreClientMono), "OnLeft"); Method(typeof(DialogUIManager), "DisplayClientText", new[] { typeof(Dialogue) });
        Method(typeof(Dialogue), "SetText", new[] { typeof(string), typeof(string) });
        Method(typeof(GameItemElement), "ResolveItemSprite"); Method(typeof(RenderHandler), "LoadFromFile"); Method(typeof(SpriteDict), "GetSprite");
        Method(typeof(DialogUIManager), "Start"); Method(typeof(DialogUIManager), "SetNormalMode"); Method(typeof(DialogUIManager), "SetShootingMode");
        Method(typeof(ModHelper), "SetModData"); Method(typeof(ModHelper), "GetModData");
        Method(typeof(GeneralHelper), "SetItemOwned");
        Method(typeof(StoreUIManager), "RunMorningDialogue");
        Method(typeof(LocHelper), "Get");
        Method(typeof(Dialogue), "SetText", new[] { typeof(string), typeof(RichTextBuilder) });
        Method(typeof(Dialogue), "SetDummy"); Method(typeof(Dialogue), "SetNextDialogue");
        Method(typeof(PlayerStore), "SaveGame"); Method(typeof(PlayerStore), "TryCallNextClient");
        Method(typeof(StoreClientManager), "TrackClientVisit"); Method(typeof(RunStats), "OnCustomerArrived");
        Method(typeof(PlayerStore), "OnCriminalVisit"); Method(typeof(ClientSpriteVariety), "OnClientShown"); Method(typeof(TutorialUIManager), "NoMoreCustomer");
        string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("dist/player.portraitruntime");
        var allowed = System.IO.Directory.GetFiles(managed, "*.dll").Select(Path.GetFileNameWithoutExtension).Concat(new[] { "PortraitRuntime.Core", "PortraitRuntime.Unity", "LaunchGiftPortraits.Content" }).ToArray();
        var scan = ModScanner.Scan(new[] { Path.Combine(root, "PortraitRuntime.Core.dll"), Path.Combine(root, "PortraitRuntime.Unity.dll") }, allowed, root, true);
        string addonRoot = Path.Combine(System.IO.Directory.GetParent(root)!.FullName, "player.launchgiftportraits");
        var addonScan = ModScanner.Scan(new[] { Path.Combine(addonRoot, "LaunchGiftPortraits.Content.dll") }, allowed, addonRoot, true);
        var xml = System.Xml.Linq.XDocument.Load(Path.Combine(addonRoot, "manifest.xml"));
        bool dependency = xml.Descendants("Prerequisites").Elements("Mod").Any(x => x.Value == "player.portraitruntime");
        bool noArt = !System.IO.Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories).Any() && !System.IO.Directory.EnumerateFiles(root, "pack.json", SearchOption.AllDirectories).Any();
        ok &= dependency && noArt;
        checks.Add(new { target = "beauty declares dependency and runtime ships no artwork", found = dependency && noArt });
        var messages = new List<string>();
        var alone = PortraitRuntime.Core.PackReader.Load(Path.Combine(root, "packs"), messages.Add);
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(addonRoot, "LaunchGiftPortraits.Content.dll"));
        var bridge = (IMod)Activator.CreateInstance(assembly.GetType("LaunchGiftPortraits.Content.BeautyContentMod")!)!;
        bridge.Init(new ModManifest { ID = "player.launchgiftportraits" }); bridge.OnEnable();
        var both = PortraitRuntime.Core.PackReader.LoadSources(new[] { Path.Combine(root, "packs") }.Concat(PortraitRuntime.Core.ContentSources.Snapshot.Values), messages.Add);
        bool registered = alone.Characters.Count == 0 && both.Characters.Count == 17 && both.Characters.Sum(x => x.Value.Value.Story.Count) == 669;
        bridge.OnDisable();
        registered &= !PortraitRuntime.Core.ContentSources.Snapshot.ContainsKey("player.launchgiftportraits");
        ok &= registered; checks.Add(new { target = "independent beauty bridge enable/register and disable/detach", found = registered });
        var settings = new PortraitRuntime.Unity.Settings();
        bool keys = settings.DebugHotkeyCode == UnityEngine.KeyCode.F7;
        settings.DebugHotkey = "F8"; keys &= settings.DebugHotkeyCode == UnityEngine.KeyCode.F8;
        settings.DebugHotkey = "invalid"; keys &= settings.DebugHotkeyCode == UnityEngine.KeyCode.F7;
        ok &= keys; checks.Add(new { target = "debug shortcut defaults F7, supports F8 and invalid fallback", found = keys });
        var result = new { passed = ok && !scan.Refused && !addonScan.Refused, runtimeExecuted = false, checks,
            notExecuted = new[] { "Native dialogue construction requires RenderHandler.current.windowUICanvas in a running Unity scene." },
            scanner = new { scan.Refused, scan.MethodsScanned, scan.Error, scan.Capabilities, scan.HardFlags, findings = scan.FormatFindings(40) },
            addonScanner = new { addonScan.Refused, addonScan.MethodsScanned, addonScan.Error, addonScan.Capabilities, addonScan.HardFlags, findings = addonScan.FormatFindings(20) } };
        string json = System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
        Console.WriteLine(json);
        System.IO.Directory.CreateDirectory("artifacts"); File.WriteAllText("artifacts/native-contracts.json", json);
        return ok && !scan.Refused && !addonScan.Refused ? 0 : 1;
    }
}
