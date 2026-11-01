using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using HarmonyLib;
using Newtonsoft.Json;
using PortraitRuntime.Core;
using UnityEngine;
using UObj = UnityEngine.Object;

namespace PortraitRuntime.Unity;

public sealed class RuntimeMod : IMod
{
    public const string ModId = "player.portraitruntime";
    public const string SupportedHash = "6398d44aa1675e1f02b63bc668868f970e0f5d3ae5a50f2aced8d06ca084e3b5";
    public static RuntimeMod Current { get; private set; }
    public Catalog Catalog { get; private set; } = new();
    public readonly NativeHost Host = new();
    public readonly SpriteCache Assets = new();
    public readonly PortraitDisplay Portrait = new();
    public readonly Settings Settings = new();
    public string Root { get; private set; }
    public bool Ready { get; private set; }
    public bool Enabled { get; private set; }
    Harmony harmony;
    RuntimePump pump;
    ModManifest manifest;
    ISet<string> dialogueKeys;
    public readonly DebugConsole Debugger = new();
    public TraceLog Trace { get; private set; }
    static readonly HashSet<string> discoveredContent = new(StringComparer.Ordinal);
    static readonly Dictionary<string, (DateTime Last, int Count)> repeatedErrors = new();
    readonly Dictionary<string, Func<StoreClient>> replacedFactories = new();
    readonly Dictionary<string, Func<StoreClient>> actorFactories = new();
    readonly Dictionary<ModItemDirectory, Dictionary<string, Func<GameItem>>> itemFactories = new();
    public readonly ConditionalWeakTable<StoreClient, FactoryTag> Origins = new();
    public readonly ConditionalWeakTable<Dialogue, DialogueTag> DialogueTags = new();
    public readonly ConditionalWeakTable<string, TextTag> TextTags = new();
    public readonly ConditionalWeakTable<RichTextBuilder, TextTag> BuilderTags = new();
    public sealed class FactoryTag { public string Factory; }
    public sealed class DialogueTag { public string Key; public Pack Pack; public string Portrait; }
    public sealed class TextTag { public readonly HashSet<string> Keys = new(StringComparer.Ordinal); }
    public static void Event(string level, string name, object data = null)
        => Current?.Trace?.Write(level, name, new { frame = Time.frameCount, day = StoreStation.Instance?.dayCounter, data });
    public static void Log(string text) { Debug.Log("[PortraitRuntime] " + text); Event("info", "runtime.message", new { message = text }); }
    public static void Guard(string scope, Action action)
    {
        try { action(); }
        catch (Exception e)
        {
            string key = scope + "/" + e.GetType().FullName + "/" + e.Message;
            repeatedErrors.TryGetValue(key, out var prior);
            if ((DateTime.UtcNow - prior.Last).TotalSeconds < 5) { repeatedErrors[key] = (prior.Last, prior.Count + 1); return; }
            Event("error", "runtime.exception", new { scope, exception = e.ToString(), suppressedDuplicates = prior.Count });
            Debug.LogError("[PortraitRuntime] " + scope + ": " + e);
            if (repeatedErrors.Count > 256) repeatedErrors.Clear();
            repeatedErrors[key] = (DateTime.UtcNow, 0);
        }
    }
    public void Init(ModManifest manifest)
    {
        this.manifest = manifest;
        Root = Path.GetDirectoryName(typeof(RuntimeMod).Assembly.Location);
        Current = this;
        Trace?.Dispose(); Trace = new TraceLog(Path.Combine(Application.persistentDataPath, "PortraitRuntime", "logs"));
        if (Trace.Failure != null) Debug.LogWarning("[PortraitRuntime] File logging unavailable: " + Trace.Failure);
        Event("info", "runtime.init", new { runtime = "0.2.1", Root, manifest.ID });
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(typeof(StoreClient).Assembly.Location);
        var hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        if (hash != SupportedHash) { Log("Unsupported game assembly " + hash + "; runtime disabled"); return; }
        if (HasLegacyConflict())
        { Log("Legacy portrait mod enabled; disable it before enabling PortraitRuntime. No hooks installed."); return; }
        Settings.Load(Path.Combine(Root, "ui.json"));
        Trace.Detailed = Settings.DetailedLogging;
        var keysPath = Path.Combine(Root, "catalogs", "dialogue_keys.json");
        dialogueKeys = File.Exists(keysPath) ? JsonConvert.DeserializeObject<HashSet<string>>(File.ReadAllText(keysPath)) : null;
        RefreshContentSources();
        Catalog = ReadCatalog();
        Assets.Budget = Settings.CacheMiB * 1024L * 1024;
        harmony = new Harmony(ModId + ".runtime");
        Hooks.Install(harmony);
        Ready = true;
        Log($"Loaded {Catalog.Packs.Count} packs; {Catalog.Characters.Count} characters, {Catalog.Items.Count} items, {Catalog.Actors.Count} actors");
    }
    public void OnEnable()
    {
        if (!Ready && manifest != null) Init(manifest);
        if (!Ready || Enabled) return;
        Enabled = true;
        ContentSources.Changed += ContentChanged;
        StoryEngine.Diagnostic += StoryDiagnostic;
        var go = new GameObject("PortraitRuntime.Lifecycle");
        UObj.DontDestroyOnLoad(go);
        pump = go.AddComponent<RuntimePump>();
        ModHook.OnModItemDirectoryInit += RegisterItems;
        ModHook.OnGenerateCustomerNormal += Generate;
        ModHook.OnGameLoadedNormal += Loaded;
        ModHook.OnGameLoadedLate += ResumeActors;
        ModHook.OnNewGameStarted += NewGame;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += SceneLoaded;
        SpecialDisplay.Apply(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        RegisterActors();
        foreach (var directory in UObj.FindObjectsOfType<ModItemDirectory>()) RegisterItems(directory);
        Log("Enabled; F7 debug console, F10 display settings, F11 diagnostics");
    }
    public void OnDisable()
    {
        Debugger.CancelImmediately();
        Enabled = false;
        ContentSources.Changed -= ContentChanged;
        StoryEngine.Diagnostic -= StoryDiagnostic;
        ModHook.OnModItemDirectoryInit -= RegisterItems;
        ModHook.OnGenerateCustomerNormal -= Generate;
        ModHook.OnGameLoadedNormal -= Loaded;
        ModHook.OnGameLoadedLate -= ResumeActors;
        ModHook.OnNewGameStarted -= NewGame;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= SceneLoaded;
        SpecialDisplay.Restore();
        RemoveOwnedFactories();
        Portrait.Restore();
        Settings.Restore();
        harmony?.UnpatchAll(harmony.Id);
        Assets.RestoreItemOwners();
        if (pump != null) UObj.Destroy(pump.gameObject);
        // Loaded game objects may still reference cached sprites; only release unbound assets.
        Assets.Trim(true);
        Event("info", "runtime.disabled", new { droppedLogs = Trace?.Dropped, logFailure = Trace?.Failure });
        Trace?.Dispose();
        Ready = false;
        Current = null;
    }
    void RemoveOwnedFactories()
    {
        foreach (var pair in replacedFactories)
            if (StoreClientListDict.storeClientDict.TryGetValue(pair.Key, out var current) && actorFactories.TryGetValue(pair.Key, out var owned) && current == owned)
            { if (pair.Value == null) StoreClientListDict.storeClientDict.Remove(pair.Key); else StoreClientListDict.storeClientDict[pair.Key] = pair.Value; }
        replacedFactories.Clear();
        actorFactories.Clear();
        foreach (var dir in itemFactories)
            if (dir.Key != null) foreach (var factory in dir.Value)
                if (dir.Key.factoryDictionary.TryGetValue(factory.Key, out var current) && current == factory.Value) dir.Key.Remove(factory.Key);
        itemFactories.Clear();
    }
    public static bool HasLegacyConflict() => ModLoader.Mods.Any(x => x.Manifest?.ID == "player.launchgiftportraits" && x.IsEnabled && !x.Manifest.Prerequisites.Contains(ModId));
    public static void RefreshContentSources()
    {
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mod in ModLoader.Mods.ToArray())
        {
            if (!mod.IsEnabled || mod.Manifest == null || !mod.Manifest.Prerequisites.Contains(ModId) || string.IsNullOrEmpty(mod.FolderPath)) continue;
            string packs = Path.Combine(mod.FolderPath, "packs");
            if (!File.Exists(Path.Combine(mod.FolderPath, "portrait-content.marker")) || !System.IO.Directory.Exists(packs)) continue;
            active.Add(mod.Manifest.ID);
            ContentSources.Register(mod.Manifest.ID, packs);
        }
        foreach (string id in discoveredContent.Where(x => !active.Contains(x)).ToArray()) ContentSources.Unregister(id);
        discoveredContent.Clear(); discoveredContent.UnionWith(active);
    }
    void StoryDiagnostic(string name, object data) => Event(name.Contains("pending") ? "error" : "debug", name, data);
    Catalog ReadCatalog()
    {
        var sources = new[] { Path.Combine(Root, "packs") }.Concat(ContentSources.Snapshot.Values).ToArray();
        Event("info", "content.load.begin", new { sources });
        var result = PackReader.LoadSources(sources, Log, dialogueKeys);
        Event("info", "content.load.complete", new { packs = result.Packs.Select(x => new { x.Id, x.Root, x.Priority }), conflicts = result.Conflicts });
        return result;
    }
    void ContentChanged() => Guard("content registration", () =>
    {
        if (!Enabled) return;
        Debugger.CancelImmediately();
        Portrait.Restore(); SpecialDisplay.Restore(); RemoveOwnedFactories();
        Catalog = ReadCatalog();
        RegisterActors();
        foreach (var directory in UObj.FindObjectsOfType<ModItemDirectory>()) RegisterItems(directory);
        Assets.RefreshItemOwners();
        SpecialDisplay.Apply(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Portrait.Arrival();
        Log("Content sources refreshed: " + Catalog.Packs.Count + " packs");
    });
    void NewGame(string id) => Loaded();
    void SceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => Guard("special", () => SpecialDisplay.Apply(scene));
    void Loaded() => Guard("load", () =>
    {
        Debugger.CancelImmediately();
        RefreshContentSources();
        RegisterActors();
        foreach (var directory in UObj.FindObjectsOfType<ModItemDirectory>()) RegisterItems(directory);
        Host.CheckDependencies();
    });
    public void RegisterItems(ModItemDirectory directory) => Guard("items", () =>
    {
        if (!itemFactories.TryGetValue(directory, out var owned)) itemFactories[directory] = owned = new();
        foreach (var entry in Catalog.ItemCreators.Values)
        {
            var captured = entry;
            if (!directory.Has(entry.Id)) { Func<GameItem> factory = () => MakeItem(captured); if (directory.Add(entry.Id, factory)) owned[entry.Id] = factory; }
        }
    });
    GameItem MakeItem(Entry<ItemDef> entry)
    {
        Event("debug", "item.create", new { pack = entry.Pack.Id, entry.Id, entry.Value.Grid, entry.Value.Value });
        var d = entry.Value;
        var item = new GameItemElement(entry.Id, d.Atlas, d.Sprite);
        item.name = d.Name; item.shortDescription = d.Description;
        item.SetValue(d.Value);
        var cells = Enumerable.Repeat((byte)1, d.Grid[0] * d.Grid[1]).ToArray();
        item.SetShape(new GridShapeBuilder().SetData(cells, d.Grid[0]));
        foreach (var tag in d.Tags) item.SetGameItemType(tag);
        item.ValidatePartialSprite();
        return item;
    }
    public void RegisterActors()
    {
        foreach (var entry in Catalog.Actors.Values)
        {
            if (replacedFactories.ContainsKey(entry.Id)) continue;
            StoreClientListDict.storeClientDict.TryGetValue(entry.Id, out var original);
            if (original != null) { Log("Actor ID already registered: " + entry.Id); continue; }
            replacedFactories[entry.Id] = original;
            var captured = entry;
            Func<StoreClient> factory = () => ActorBuilder.Create(this, captured);
            StoreClientListDict.storeClientDict[entry.Id] = factory;
            actorFactories[entry.Id] = factory;
        }
    }
    void ResumeActors() => Guard("resume actors", () =>
    {
        if (Debugger.Active) return;
        foreach (var e in Catalog.Actors.Values)
            if (Host.Get(e.Pack.Id + "/_active/" + e.Id) == "yes") Host.QueueActor(e.Id);
    });
    void Generate(StoreClientManager manager) => Guard("generate", () =>
    {
        if (Debugger.Active) return;
        foreach (var e in Catalog.Actors.Values)
        {
            var key = e.Pack.Id + "/_actor/" + e.Id;
            if (Host.Get(e.Pack.Id + "/_active/" + e.Id) == "yes" || ((!e.Value.Once || Host.Get(key) != "visited") && StoryEngine.Matches(e.Pack, e.Value.When, Host))) Host.QueueActor(e.Id);
        }
    });
    public void Diagnostics()
    {
        var blueprint = PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint();
        var value = new { gameHash = SupportedHash, runtime = "0.2.1", sources = ContentSources.Snapshot, debug = Debugger.Summary(), packs = Catalog.Packs.Select(x => new { x.Id, x.Priority }), conflicts = Catalog.Conflicts,
            client = blueprint == null ? null : new { blueprint.identifier, blueprint.spriteName, factory = Origins.TryGetValue(blueprint, out var f) ? f.Factory : "" },
            cacheBytes = Assets.Bytes, cacheEntries = Assets.Count, traceSession = Trace?.Session, logs = Trace?.Files, droppedLogs = Trace?.Dropped, logFailure = Trace?.Failure,
            timestamp = DateTime.UtcNow.ToString("O"), stage = "runtime-user-initiated" };
        var path = Path.Combine(Trace.DirectoryPath, "diagnostics-" + Trace.Session + ".json");
        File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
        Log("Diagnostics written to " + path);
    }
}
public sealed class RuntimePump : MonoBehaviour
{
    void Update()
    {
        var r = RuntimeMod.Current; if (r == null || !r.Enabled) return;
        RuntimeMod.Guard("tick", () =>
        {
            if (Time.frameCount % 120 == 0 && RuntimeMod.HasLegacyConflict()) { RuntimeMod.Log("Old standalone beauty Mod conflicts with runtime. Install the dependent content edition."); r.OnDisable(); return; }
            if (Time.frameCount % 120 == 0) RuntimeMod.RefreshContentSources();
            if (Input.GetKeyDown(r.Settings.DebugHotkeyCode)) r.Debugger.Open = !r.Debugger.Open;
            if (Input.GetKeyDown(r.Settings.HotkeyCode)) r.Settings.Open = !r.Settings.Open;
            if (Input.GetKeyDown(KeyCode.F11)) r.Diagnostics();
            if (Input.GetKeyDown(KeyCode.Escape)) { r.Settings.Open = false; r.Debugger.Open = false; }
            r.Debugger.Tick();
        });
    }
    void LateUpdate() => RuntimeMod.Guard("display", () => RuntimeMod.Current?.Settings.Apply());
    void OnGUI() { var r = RuntimeMod.Current; if (r?.Settings.Open == true) r.Settings.Draw(); if (r != null && (r.Debugger.Open || r.Debugger.Active)) r.Debugger.Draw(); }
}
