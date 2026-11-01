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
        Method(typeof(ModHelper), "SetModData"); Method(typeof(ModHelper), "GetModData");
        Method(typeof(GeneralHelper), "SetItemOwned");
        Method(typeof(StoreUIManager), "RunMorningDialogue");
        Method(typeof(MapUIManager), "ActivateClientOutsideStore");
        Method(typeof(StoreClientMono), "StartLeavingStore");
        Method(typeof(StoreClientMono), "StartLeavingStoreRight");
        Method(typeof(CustomUIManager), "CreateWindow");
        Method(typeof(CustomUIManager), "GetLayerRoot");
        Method(typeof(LocHelper), "Get");
        Method(typeof(Dialogue), "SetText", new[] { typeof(string), typeof(RichTextBuilder) });
        Method(typeof(Dialogue), "SetDummy"); Method(typeof(Dialogue), "SetNextDialogue");
        Method(typeof(PlayerStore), "SaveGame"); Method(typeof(PlayerStore), "TryCallNextClient");
        Method(typeof(StoreClientManager), "TrackClientVisit"); Method(typeof(RunStats), "OnCustomerArrived");
        Method(typeof(PlayerStore), "OnCriminalVisit"); Method(typeof(ClientSpriteVariety), "OnClientShown"); Method(typeof(TutorialUIManager), "NoMoreCustomer");
        string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("dist/player.portraitruntime");
        // Mirror the game's startup cache using only its Managed directory, never Mod DLLs.
        var gameNames = new HashSet<string>(System.IO.Directory.GetFiles(managed, "*.dll", SearchOption.AllDirectories).Select(Path.GetFileNameWithoutExtension)!, StringComparer.OrdinalIgnoreCase);
        typeof(ModLoader).GetField("gameAssemblyNames", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, gameNames);
        string[] Listed(string folder) => XDocument.Load(Path.Combine(folder, "manifest.xml")).Root!.Element("Assemblies")!.Elements("Assembly").Select(x => x.Value).ToArray();
        var scan = ModScanner.Scan(Listed(root).Select(x => Path.Combine(root, x)).ToArray(), Listed(root).Select(Path.GetFileNameWithoutExtension).ToArray(), root, true);
        string addonRoot = Path.Combine(System.IO.Directory.GetParent(root)!.FullName, "player.launchgiftportraits");
        var addonScan = ModScanner.Scan(Listed(addonRoot).Select(x => Path.Combine(addonRoot, x)).ToArray(), Listed(addonRoot).Select(Path.GetFileNameWithoutExtension).ToArray(), addonRoot, true);
        // Negative control: the same DLL set with the core's own unlisted reference must be refused,
        // which is the failure mode that forces the JSON bridge instead of a shared assembly.
        var controlScan = ModScanner.Scan(new[] { Path.Combine(root, "PortraitRuntime.Unity.dll") }, new[] { "PortraitRuntime.Unity" }, root, true);
        var addonReferences = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(addonRoot, "LaunchGiftPortraits.Content.dll")).GetReferencedAssemblies().Select(x => x.Name!).ToArray();
        bool noRuntimeReference = !addonReferences.Any(x => x.StartsWith("PortraitRuntime", StringComparison.Ordinal));
        checks.Add(new { target = "beauty Mod loads with no core assembly reference (unlisted reference is refused)", found = controlScan.Refused && !addonScan.Refused && noRuntimeReference, references = addonReferences });
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
        // Handshake: the content Mod must be able to reach the core API through reflection only.
        var api = typeof(PortraitRuntime.Unity.RuntimeMod).Assembly.GetType("PortraitRuntime.Unity.PortraitApi")!;
        object Property(string name) => api.GetProperty(name, BindingFlags.Public | BindingFlags.Static).GetValue(null);
        var capabilities = (string[])Property("Capabilities")!;
        int subscribersBound = (int)Property("SubscriberCount")!;
        api.GetMethod("Raise", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { "test.ping", new { n = 1 } });
        var bridgeType = assembly.GetType("LaunchGiftPortraits.Content.BeautyContentMod")!;
        string lastEvent = (string)bridgeType.GetProperty("LastEvent", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        bool handshake = (int)Property("Version")! == 2 && capabilities.Contains("lifecycle/1") && capabilities.Contains("lookup/1") && capabilities.Contains("assets/1")
            && subscribersBound == 1 && lastEvent.StartsWith("test.ping", StringComparison.Ordinal);
        string status = (string)api.GetMethod("Status", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
        bool resolvers = status.Contains("\"spriteResolver\":true", StringComparison.Ordinal) && status.Contains("\"itemResolver\":true", StringComparison.Ordinal);
        handshake &= resolvers;
        ok &= handshake;
        checks.Add(new { target = "beauty Mod binds the core API by reflection and registers the replacement resolvers", found = handshake, version = Property("Version"), capabilities, lastEvent });
        foreach (var member in new[] { "ApplyTo", "ReleaseTo", "SetSpriteResolver", "SetItemResolver" })
        {
            var found = api.GetMethod(member, BindingFlags.Public | BindingFlags.Static) != null;
            ok &= found;
            checks.Add(new { target = "PortraitApi." + member, found });
        }
        var loaded = new LoadedMod { Manifest = new ModManifest { ID = "player.launchgiftportraits", Prerequisites = new List<string> { "player.portraitruntime" } }, FolderPath = addonRoot, Instance = bridge, IsEnabled = true };
        ModLoader.Mods.Add(loaded);
        PortraitRuntime.Unity.RuntimeMod.RefreshContentSources();
        var both = PortraitRuntime.Core.PackReader.LoadSources(new[] { Path.Combine(root, "packs") }.Concat(PortraitRuntime.Core.ContentSources.Snapshot.Values), messages.Add);
        bool registered = alone.Characters.Count == 0 && both.Characters.Count == 17 && both.Characters.Sum(x => x.Value.Value.Story.Count) == 669;
        bridge.OnDisable();
        int subscribersAfter = (int)Property("SubscriberCount")!;
        string statusAfter = (string)api.GetMethod("Status", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
        bool detached = subscribersAfter == 0 && statusAfter.Contains("\"spriteResolver\":false", StringComparison.Ordinal) && statusAfter.Contains("\"itemResolver\":false", StringComparison.Ordinal);
        ok &= detached;
        checks.Add(new { target = "disabling the beauty Mod releases its subscription and resolvers", found = detached });
        loaded.IsEnabled = false;
        PortraitRuntime.Unity.RuntimeMod.RefreshContentSources();
        ModLoader.Mods.Remove(loaded);
        registered &= !PortraitRuntime.Core.ContentSources.Snapshot.ContainsKey("player.launchgiftportraits");
        ok &= registered; checks.Add(new { target = "core discovers enabled beauty content and detaches disabled content", found = registered });
        ModLoader.Mods.Add(loaded);
        loaded.IsEnabled = true; loaded.Manifest.Prerequisites.Clear();
        PortraitRuntime.Unity.RuntimeMod.RefreshContentSources();
        bool noDependency = !PortraitRuntime.Core.ContentSources.Snapshot.ContainsKey(loaded.Manifest.ID);
        loaded.Manifest.Prerequisites.Add("player.portraitruntime"); loaded.FolderPath = root;
        PortraitRuntime.Unity.RuntimeMod.RefreshContentSources();
        bool noMarker = !PortraitRuntime.Core.ContentSources.Snapshot.ContainsKey(loaded.Manifest.ID);
        ModLoader.Mods.Remove(loaded);
        ok &= noDependency && noMarker;
        checks.Add(new { target = "unmarked Mods and Mods without core dependency are not admitted", found = noDependency && noMarker });
        // Display preferences now belong to the content Mod: defaults must leave native transforms alone.
        var displayType = assembly.GetType("LaunchGiftPortraits.Content.DisplaySettings")!;
        var display = Activator.CreateInstance(displayType)!;
        object Read(string name) => displayType.GetProperty(name)!.GetValue(display)!;
        void Write(string name, object value) => displayType.GetProperty(name)!.SetValue(display, value);
        bool adjustment = !(bool)Read("HasPortraitAdjustment");
        Write("PortraitX", 1f); adjustment &= (bool)Read("HasPortraitAdjustment");
        Write("PortraitX", 0f); Write("PortraitScale", 1.2f); adjustment &= (bool)Read("HasPortraitAdjustment");
        Write("PortraitScale", 1f); adjustment &= !(bool)Read("HasPortraitAdjustment");
        Write("BubbleX", 900f); displayType.GetMethod("Normalize")!.Invoke(display, null);
        adjustment &= (float)Read("BubbleX") == 600f;
        ok &= adjustment; checks.Add(new { target = "default display settings leave native portrait transforms alone", found = adjustment });
        var settings = new PortraitRuntime.Unity.Settings();
        bool keys = settings.DebugHotkeyCode == UnityEngine.KeyCode.F3;
        settings.DebugHotkey = "F4"; keys &= settings.DebugHotkeyCode == UnityEngine.KeyCode.F4;
        settings.DebugHotkey = "F7"; keys &= settings.DebugHotkeyCode == UnityEngine.KeyCode.F3;
        settings.DebugHotkey = "invalid"; keys &= settings.DebugHotkeyCode == UnityEngine.KeyCode.F3;
        ok &= keys; checks.Add(new { target = "debug shortcut defaults F3, supports F4 and migrates old keys", found = keys });
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
