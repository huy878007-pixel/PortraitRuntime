using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using PortraitRuntime.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PortraitRuntime.Unity;

/// <summary>In-game, same-day visual tour; real story execution is a separate explicit mode.</summary>
public sealed class DebugConsole
{
    public bool Open;
    public bool Active => owner != null;
    public bool Preview => Active && !realMode;
    readonly DebugTourPlan plan = new();
    readonly HashSet<StoreClient> synthetic = new();
    readonly List<object> results = new();
    List<DebugCase> registry;
    List<StoreClient> originalQueue;
    StoreClientManager manager;
    PlayerStore owner;
    StoreClientInstance oldLastVisited;
    StoreClient current;
    bool oldCallNext, pending, stopping, realMode, arrived, paused, automatic, permitReal;
    int day;
    float arrivedAt;
    string message = "打开营业后的测试存档，选择巡览范围。", search = "", note = "", resolution = "exact";
    Rect window = new(25, 25, 960, 720);
    Rect hud;
    Vector2 scroll;
    GameObject hudBlocker;
    public DebugCase CurrentCase => plan.Current;
    public bool IsSynthetic(StoreClient client) => Preview && client != null && synthetic.Contains(client);
    public bool BlocksSave => Active && ReferenceEquals(owner, PlayerStore.Instance) && day == StoreStation.Instance?.dayCounter;
    public object Summary() => new { active = Active, mode = realMode ? "real-story" : "visual-preview", day, index = plan.Index, count = plan.Cases.Count, current = plan.Current, pending, paused, message, results = results.Count };
    RuntimeMod Runtime => RuntimeMod.Current;
    IReadOnlyList<DebugCase> Registry
    {
        get
        {
            if (registry == null)
            {
                var path = Path.Combine(Runtime.Root, "catalogs", "debug_visits.json");
                registry = File.Exists(path) ? JsonConvert.DeserializeObject<List<DebugCase>>(File.ReadAllText(path)) : new();
            }
            return registry;
        }
    }
    List<DebugCase> Named(bool stories)
    {
        var list = new List<DebugCase>();
        foreach (var e in Runtime.Catalog.Characters.Values)
        {
            var c = e.Value;
            if (stories)
            {
                foreach (var key in c.Story.Keys.OrderBy(x => x, StringComparer.Ordinal))
                    list.Add(new() { Id = "story/" + e.Pack.Id + "/" + e.Id + "/" + key, Label = e.Id + " · " + key,
                        PackId = e.Pack.Id, CharacterId = e.Id, Identity = c.Identifiers.FirstOrDefault(), Factory = c.Factories.FirstOrDefault(),
                        SpriteKey = c.Variants.Keys.FirstOrDefault(), DialogueKey = key });
            }
            else
            {
                var variants = c.Variants.Count == 0 ? new[] { "" } : c.Variants.Keys.ToArray();
                foreach (var key in variants)
                    list.Add(new() { Id = "character/" + e.Pack.Id + "/" + e.Id + "/" + key, Label = e.Id + (key.Length == 0 ? "" : " · " + key),
                        PackId = e.Pack.Id, CharacterId = e.Id, Identity = c.Identifiers.FirstOrDefault(), Factory = c.Factories.FirstOrDefault(), SpriteKey = key });
            }
        }
        return list;
    }
    List<DebugCase> Sprites() => SpriteDict.Instance == null ? new() : SpriteDict.Instance.spriteDictionary.Where(x => x.Value != null)
        .OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new DebugCase { Id = "sprite/" + x.Key, Label = "原版精灵池 · " + x.Key, SpriteKey = x.Key }).ToList();
    List<DebugCase> All() => Registry.Concat(Named(false)).Concat(Sprites()).ToList();
    public void Tick()
    {
        UpdateHudBlocker();
        if (!Active) return;
        if (!ReferenceEquals(owner, PlayerStore.Instance) || StoreStation.Instance?.dayCounter != day)
        { message = "存档或日期发生变化，本轮巡览已结束。"; CancelImmediately(); return; }
        if (pending && owner.currentClientInstance == null && StoreClientMono.Instance != null && !StoreClientMono.Instance.IsMoving())
        {
            if (stopping) { Finish(); return; }
            if (paused) return;
            ShowNext();
        }
        if (automatic && !paused && !pending && arrived && Time.unscaledTime - arrivedAt >= 4f) Next();
    }
    void Request(IEnumerable<DebugCase> cases, bool real = false)
    {
        if (Active) { message = "请先结束当前巡览。"; return; }
        var player = PlayerStore.Instance;
        if (player == null || StoreStation.Instance == null || StoreUIManager.Instance == null || StoreClientMono.Instance == null) { message = "请先加载测试存档。"; return; }
        if (player.storeState != PlayerStore.StoreState.OPEN) { message = "请先打开店铺营业，再开始当天巡览。"; return; }
        if (player.currentClientInstance != null || StoreClientMono.Instance.IsMoving()) { message = "请先送走当前客人，待离场动画结束再开始。"; return; }
        var entries = cases.ToArray();
        if (entries.Length == 0) { message = "当前范围没有条目；请确认人物美化 Mod 已启用。"; return; }
        plan.Start(entries); owner = player; day = StoreStation.Instance.dayCounter; realMode = real;
        manager = player.storeClientManager;
        originalQueue = manager.clientStack; manager.clientStack = new();
        oldCallNext = player.callNextWhenPossible; oldLastVisited = player.lastVisitedClient;
        player.callNextWhenPossible = false; synthetic.Clear(); results.Clear(); pending = true; stopping = false; paused = false;
        RuntimeMod.Event("info", "debug.tour.start", new { mode = real ? "real-story" : "visual-preview", day, cases = entries.Length, preservedQueue = originalQueue.Count });
        if (real) automatic = false;
        message = "巡览已开始；当天逐位进场。"; Open = false;
    }
    void PreserveUnexpectedQueue()
    {
        foreach (var client in owner.storeClientManager.clientStack)
            if (!ReferenceEquals(client, current) && !synthetic.Contains(client) && !originalQueue.Contains(client)) originalQueue.Add(client);
        owner.storeClientManager.clientStack.Clear();
    }
    void ShowNext()
    {
        var spec = plan.Next();
        if (spec == null) { Finish(); return; }
        PreserveUnexpectedQueue();
        current = null; arrived = false;
        try
        {
            if (realMode)
            {
                if (spec.RegistrationId == null || !StoreClientListDict.storeClientDict.TryGetValue(spec.RegistrationId, out var factory)) throw new InvalidOperationException("Registration unavailable: " + spec.RegistrationId);
                current = factory();
                if (current == null) throw new InvalidOperationException("Factory returned null");
                current.dismissable = true;
            }
            else current = MakePreview(spec);
            owner.storeClientManager.clientStack.Add(current);
            pending = false;
            RuntimeMod.Event("debug", "debug.case.queued", new { index = plan.Index, spec, current.identifier, current.spriteName, resolution, realMode });
            owner.TryCallNextClient();
            if (!ReferenceEquals(owner.currentClientInstance?.GetClientBlueprint(), current)) throw new InvalidOperationException("Native arrival could not start");
        }
        catch (Exception e)
        {
            RuntimeMod.Event("error", "debug.case.failed", new { index = plan.Index, spec, exception = e.ToString() });
            results.Add(new { spec.Id, outcome = "construction-failed", exception = e.ToString() });
            owner.storeClientManager.clientStack.Remove(current); current = null; pending = true;
            message = "此条目失败，已记录并继续：" + spec.Id;
        }
    }
    StoreClient MakePreview(DebugCase spec)
    {
        var sprite = ResolveSprite(spec, out resolution);
        var client = new StoreClient { identifier = Catalog.HasIdentity(spec.Identity) ? spec.Identity : "NO_ID", displayName = "[巡览] " + spec.Label,
            spriteName = sprite, clientIntent = StoreClient.ClientIntent.DIALOGUE, dismissable = true, arrestable = false, nonShootable = true,
            isBargainAvailable = false, sourceModId = RuntimeMod.ModId };
        string text = spec.Text ?? "外观巡览，不执行此角色的原剧情。";
        client.mainDialogue.SetText("CLIENT", text + "\n" + spec.Id);
        if (spec.Factory != null) Runtime.Origins.Add(client, new RuntimeMod.FactoryTag { Factory = spec.Factory });
        if (spec.DialogueKey != null) Runtime.DialogueTags.Add(client.mainDialogue, new RuntimeMod.DialogueTag { Key = spec.DialogueKey });
        synthetic.Add(client);
        return client;
    }
    /// <summary>
    /// Resolve the sprite the way the game itself would. SpriteDict.GetSprite falls back to a random
    /// scavenger for unknown keys, so an unresolvable visitor must never borrow dictionary[0] (the
    /// mentor portrait) and pretend the lookup succeeded; status names the rule that actually applied.
    /// </summary>
    string ResolveSprite(DebugCase spec, out string status)
    {
        var dict = SpriteDict.Instance.spriteDictionary;
        bool Known(string key) => key != null && dict.TryGetValue(key, out var sprite) && sprite != null;
        var expressions = spec.SpriteExpressions ?? Array.Empty<string>();
        var candidates = expressions.SelectMany(Evaluate).ToList();
        if (spec.PackId != null && Runtime.Catalog.Characters.TryGetValue(spec.PackId + "/" + spec.CharacterId, out var character))
            candidates.AddRange(character.Value.Variants.Keys);
        if (SpriteResolver.TryPick(spec.SpriteKey, candidates, Known, out var sprite, out status))
        {
            if (!Known(spec.SpriteKey) && expressions.Any(x => x != null && x.Contains("GetRandom"))) status = "random-pool";
            RuntimeMod.Event("debug", "debug.sprite.resolved", new { spec.Id, status, sprite, expressions });
            return sprite;
        }
        var fallback = ScavengerFallback();
        RuntimeMod.Event("warning", "debug.sprite.fallback", new { spec.Id, status, requested = spec.SpriteKey, expressions, fallback });
        return fallback;
    }
    /// <summary>Evaluate a catalogued sprite expression; supports literals and static 0..n-string calls.</summary>
    static IEnumerable<string> Evaluate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) yield break;
        var text = expression.Trim().TrimEnd(',');
        var literal = Regex.Match(text, "^\"([^\"]*)\"$");
        if (literal.Success) { yield return literal.Groups[1].Value; yield break; }
        foreach (Match call in Regex.Matches(text, @"\b(SpriteDict|LocHelper)\.(\w+)\(([^()]*)\)"))
        {
            var type = call.Groups[1].Value == "SpriteDict" ? typeof(SpriteDict) : typeof(LocHelper);
            var args = Regex.Matches(call.Groups[3].Value, "\"([^\"]*)\"").Cast<Match>().Select(x => (object)x.Groups[1].Value).ToArray();
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                         .Where(x => x.Name == call.Groups[2].Value && x.ReturnType == typeof(string) && x.GetParameters().Length == args.Length))
            {
                string key = null;
                try { key = method.Invoke(null, args) as string; }
                catch (Exception e) { RuntimeMod.Event("error", "debug.sprite.expression.failed", new { expression, exception = e.ToString() }); }
                if (key != null) yield return key;
            }
        }
    }
    /// <summary>The native fallback for an unknown key, so the preview shows what the game would show.</summary>
    static string ScavengerFallback()
    {
        try
        {
            var method = typeof(SpriteDict).GetMethod("GetRandomScavSpriteName", BindingFlags.Public | BindingFlags.Static);
            if (method != null && method.ReturnType == typeof(string) && method.GetParameters().Length == 0 && method.Invoke(null, null) is string key && key.Length > 0) return key;
        }
        catch (Exception e) { RuntimeMod.Event("error", "debug.sprite.scavenger.failed", new { exception = e.ToString() }); }
        return "default";
    }
    public void Arrived()
    {
        if (!Active || !ReferenceEquals(owner.currentClientInstance?.GetClientBlueprint(), current)) return;
        arrived = true; arrivedAt = Time.unscaledTime;
        RuntimeMod.Event("info", "debug.case.arrived", new { index = plan.Index, spec = plan.Current, current.identifier, current.spriteName, resolution,
            displayed = Runtime.Portrait.Renderer?.sprite?.name, frame = Time.frameCount, day });
    }
    public void Left() { if (Active && current != null && owner.currentClientInstance == null && !pending) RecordAndAdvance("left"); }
    void RecordAndAdvance(string outcome)
    {
        results.Add(new { id = plan.Current?.Id, index = plan.Index, outcome, note, resolution, day, utc = DateTime.UtcNow.ToString("O") });
        RuntimeMod.Event(outcome == "issue" ? "warning" : "info", "debug.case.result", new { id = plan.Current?.Id, index = plan.Index, outcome, note });
        note = ""; pending = true; arrived = false;
    }
    public void Next() => Advance("viewed");
    void Advance(string outcome)
    {
        if (!Active || pending) return;
        RecordAndAdvance(outcome);
        if (ReferenceEquals(owner.currentClientInstance?.GetClientBlueprint(), current)) owner.DismissCurrentClient(false);
    }
    void Replay(int index)
    {
        if (!Active || pending) return;
        Advance("replay"); plan.Seek(index);
    }
    void Stop()
    {
        if (!Active) return;
        stopping = true; paused = false;
        if (!pending) RecordAndAdvance("stopped"); else pending = true;
        if (ReferenceEquals(owner.currentClientInstance?.GetClientBlueprint(), current)) owner.DismissCurrentClient(false);
    }
    void Finish()
    {
        try { Export(); }
        finally
        {
            try { RestoreQueue(); }
            finally { plan.Stop(); current = null; owner = null; originalQueue = null; pending = stopping = false; synthetic.Clear(); DestroyHudBlocker(); Open = true; }
        }
    }
    void RestoreQueue()
    {
        if (owner == null || originalQueue == null || !ReferenceEquals(owner, PlayerStore.Instance) || !ReferenceEquals(owner.storeClientManager, manager) || StoreStation.Instance?.dayCounter != day) return;
        PreserveUnexpectedQueue(); owner.storeClientManager.clientStack = originalQueue;
        owner.callNextWhenPossible = oldCallNext;
        if (!realMode) owner.lastVisitedClient = oldLastVisited;
    }
    public void CancelImmediately()
    {
        if (!Active) { DestroyHudBlocker(); Open = false; return; }
        try
        {
            if (ReferenceEquals(owner, PlayerStore.Instance) && ReferenceEquals(owner.currentClientInstance?.GetClientBlueprint(), current)) owner.DismissCurrentClient(false);
        }
        catch (Exception e) { RuntimeMod.Event("error", "debug.cancel.dismiss.failed", new { exception = e.ToString() }); }
        finally
        {
            try { Export(); }
            finally
            {
                try { RestoreQueue(); }
                finally { plan.Stop(); owner = null; originalQueue = null; current = null; synthetic.Clear(); pending = false; DestroyHudBlocker(); Open = false; }
            }
        }
    }
    public void Export()
    {
        try
        {
        var root = Runtime.Trace.DirectoryPath; System.IO.Directory.CreateDirectory(root);
        var path = Path.Combine(root, "tour-report-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".json");
        File.WriteAllText(path, JsonConvert.SerializeObject(new { runtime = RuntimeMod.Version, gameRuntime = true, summary = Summary(), cases = plan.Cases, results,
            traceSession = Runtime.Trace?.Session, logFiles = Runtime.Trace?.Files, droppedLogs = Runtime.Trace?.Dropped, traceFailure = Runtime.Trace?.Failure }, Formatting.Indented));
        RuntimeMod.Event("info", "debug.report.export", new { path, count = results.Count });
        message = "报告已导出：" + path;
        }
        catch (Exception e) { message = "报告导出失败（巡览仍可结束并恢复队列）：" + e.Message; RuntimeMod.Event("error", "debug.report.failed", new { exception = e.ToString() }); }
    }
    public void Draw()
    {
        if (Open) window = GUI.Window(7670, window, Window, "深空核心库 · 调试台（F3 / 可改 F4）");
        else if (Active)
        {
            hud = new Rect(Math.Max(0, Screen.width - 555), 20, 535, 125);
            hud = GUI.Window(7671, hud, Hud, "人物巡览 · 当天 " + day);
        }
    }
    void Window(int id)
    {
        GUILayout.Label("核心库独立运行；人物美化来自单独启用的内容 Mod。已加载 " + Runtime.Catalog.Characters.Count + " 套人物内容。");
        GUILayout.Label("巡览不运行原剧情；会使用真实来访显示路径。请用测试存档。巡览期间暂停保存，结束后恢复原队列。");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("当天全部人物巡览")) Request(All());
        if (GUILayout.Button("仅原版来访（" + Registry.Count + "）")) Request(Registry);
        if (GUILayout.Button("美化人物与阶段")) Request(Named(false));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("全部剧情配图逐项巡检")) Request(Named(true));
        if (GUILayout.Button("全部随机池精灵")) Request(Sprites());
        if (GUILayout.Button("导出诊断与巡览报告")) { Runtime.Diagnostics(); Export(); }
        GUILayout.EndHorizontal();
        bool detailed = GUILayout.Toggle(Runtime.Settings.DetailedLogging, "详细日志（人物身份、台词 key、资源命中、剧情动作、完整异常堆栈）");
        if (detailed != Runtime.Settings.DetailedLogging) { Runtime.Settings.DetailedLogging = detailed; Runtime.Settings.Save(); }
        if (Runtime.Trace != null) Runtime.Trace.Detailed = Runtime.Settings.DetailedLogging;
        GUILayout.Label("日志目录：" + Runtime.Trace?.DirectoryPath + " · 丢弃 " + (Runtime.Trace?.Dropped ?? 0));
        automatic = GUILayout.Toggle(automatic, "自动巡览：进场后停留 4 秒，再下一位");
        if (Active) Controls();
        GUILayout.Label(message);
        GUILayout.Label("检索人物 / 注册 ID / 工厂"); search = GUILayout.TextField(search);
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(270));
        foreach (var c in Registry.Where(x => (x.Label + " " + x.Id + " " + x.Factory).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).Take(80))
        {
            GUILayout.BeginHorizontal(); GUILayout.Label(c.Label + " · " + c.RegistrationId, GUILayout.Width(520));
            if (GUILayout.Button("外观预览", GUILayout.Width(105))) Request(new[] { c });
            if (permitReal && GUILayout.Button("真实来访", GUILayout.Width(105))) Request(new[] { c }, true);
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
        permitReal = GUILayout.Toggle(permitReal, "高级：允许真实来访（会执行原剧情、交易和角色回调，可能改变剧情或结束游戏）");
        if (permitReal && GUILayout.Button("当天全部真实来访（仅新建测试存档；失败条目记日志后继续）")) Request(Registry, true);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("调试键：" + Runtime.Settings.DebugHotkey + "（点击切换 F3/F4）")) { Runtime.Settings.DebugHotkey = Runtime.Settings.DebugHotkey == "F4" ? "F3" : "F4"; Runtime.Settings.Save(); }
        if (GUILayout.Button("关闭调试台")) Open = false;
        GUILayout.EndHorizontal(); GUI.DragWindow(new Rect(0, 0, 960, 24));
    }
    string ResolutionLabel()
    {
        switch (resolution)
        {
            case "native-key-missing": return "原生缺 key，已按原生规则随机拾荒者（待核对）";
            case "unresolved": return "未解析，已按原生规则随机拾荒者（待核对）";
            case "random-pool": return "原生随机池";
            default: return "精确";
        }
    }
    void Controls()
    {
        GUILayout.Label((plan.Index + 1) + " / " + plan.Cases.Count + " · " + (plan.Current?.Label ?? "等待开始"));
        if (current != null) GUILayout.Label("身份 " + current.identifier + " · 精灵 " + current.spriteName + " · 图片 " + Runtime.Portrait.Renderer?.sprite?.name + " · 解析 " + ResolutionLabel());
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("上一位")) Replay(Math.Max(0, plan.Index - 1));
        if (GUILayout.Button("重看")) Replay(Math.Max(0, plan.Index));
        if (GUILayout.Button("下一位")) Next();
        if (GUILayout.Button(paused ? "继续" : "暂停")) paused = !paused;
        if (GUILayout.Button("标记异常并下一位")) Advance("issue");
        if (GUILayout.Button("结束巡览")) Stop();
        GUILayout.EndHorizontal();
        note = GUILayout.TextField(note);
    }
    void Hud(int id) { Controls(); if (GUILayout.Button("打开完整调试台")) Open = true; }
    void UpdateHudBlocker()
    {
        if (!Active || Open) { DestroyHudBlocker(); return; }
        var layer = CustomUIManager.Instance?.GetLayerRoot("overlay"); if (layer == null) return;
        if (hudBlocker == null)
        {
            hudBlocker = new GameObject("PortraitRuntime.DebugHudBlocker", typeof(RectTransform), typeof(Image));
            var image = hudBlocker.GetComponent<Image>(); image.color = Color.clear; image.raycastTarget = true;
            hudBlocker.transform.SetParent(layer, false);
        }
        var scale = layer.GetComponentInParent<Canvas>()?.scaleFactor ?? 1;
        var rect = (RectTransform)hudBlocker.transform; rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(hud.x / scale, -hud.y / scale); rect.sizeDelta = new Vector2(hud.width / scale, hud.height / scale);
    }
    void DestroyHudBlocker() { if (hudBlocker != null) UnityEngine.Object.Destroy(hudBlocker); hudBlocker = null; }
}
