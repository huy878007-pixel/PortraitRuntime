using System.Reflection;
using HarmonyLib;
using PortraitRuntime.Core;
using UnityEngine;

namespace PortraitRuntime.Unity;

public static class Hooks
{
    static bool Active => RuntimeMod.Current?.Enabled == true;
    public static void Install(Harmony harmony)
    {
        void Patch(Type type, string method, string prefix = null, string postfix = null, Type[] arguments = null)
        {
            var target = arguments == null ? AccessTools.Method(type, method) : AccessTools.Method(type, method, arguments);
            if (target == null) throw new MissingMethodException(type.FullName, method);
            HarmonyMethod Hook(string name) => name == null ? null : new HarmonyMethod(typeof(Hooks), name) { priority = Priority.Last };
            harmony.Patch(target, Hook(prefix), Hook(postfix));
        }
        Patch(typeof(PlayerStore), "GetNextClient", nameof(BeforeNext), nameof(Arrival));
        Patch(typeof(PlayerStore), "SaveGame", prefix: nameof(SaveAllowed));
        Patch(typeof(StoreClientManager), "TrackClientVisit", prefix: nameof(PreviewStatsAllowed));
        Patch(typeof(RunStats), "OnCustomerArrived", prefix: nameof(PreviewStatsAllowed));
        Patch(typeof(PlayerStore), "OnCriminalVisit", prefix: nameof(PreviewStatsAllowed));
        Patch(typeof(ClientSpriteVariety), "OnClientShown", prefix: nameof(PreviewSideEffectAllowed));
        Patch(typeof(TutorialUIManager), "NoMoreCustomer", prefix: nameof(PreviewSideEffectAllowed));
        Patch(typeof(StoreUIManager), "OnNextClientArrived", postfix: nameof(Arrival));
        Patch(typeof(StoreClientMono), "OnLeft", postfix: nameof(Left));
        Patch(typeof(StoreUIManager), "RunMorningDialogue", prefix: nameof(Morning));
        Patch(typeof(DialogUIManager), "DisplayClientText", postfix: nameof(Display), arguments: new[] { typeof(Dialogue) });
        Patch(typeof(Dialogue), "SetText", postfix: nameof(SetText), arguments: new[] { typeof(string), typeof(string) });
        Patch(typeof(Dialogue), "SetText", postfix: nameof(SetRichText), arguments: new[] { typeof(string), typeof(RichTextBuilder) });
        Patch(typeof(LocHelper), "Get", postfix: nameof(Localized));
        foreach (var m in typeof(RichTextBuilder).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(x => x.Name == "Add" && x.GetParameters().Length > 0 && (x.GetParameters()[0].ParameterType == typeof(string) || x.GetParameters()[0].ParameterType == typeof(RichTextBuilder))))
            harmony.Patch(m, postfix: new HarmonyMethod(typeof(Hooks), nameof(BuilderAdd)));
        Patch(typeof(RichTextBuilder), "Clear", postfix: nameof(BuilderClear));
        foreach (var type in typeof(StoreClient).Assembly.GetTypes().Where(x => x.Name.StartsWith("StoreClient", StringComparison.Ordinal) && x != typeof(StoreClientListDict)))
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(x => x.ReturnType == typeof(StoreClient) && !x.ContainsGenericParameters))
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(Hooks), nameof(Factory)));
        Patch(typeof(GameItemElement), "ResolveItemSprite", postfix: nameof(ItemSprite));
        Patch(typeof(GeneralHelper), "SetItemOwned", postfix: nameof(Owned));
        Patch(typeof(RenderHandler), "LoadFromFile", postfix: nameof(Special));
        Patch(typeof(SpriteDict), "GetSprite", postfix: nameof(Special));
        foreach (var method in new[] { "Start", "SetNormalMode", "SetShootingMode" }) Patch(typeof(DialogUIManager), method, postfix: nameof(Bubble));
    }
    static void Factory(MethodBase __originalMethod, StoreClient __result)
    {
        if (!Active || __result == null) return;
        var r = RuntimeMod.Current;
        r.Origins.Remove(__result);
        r.Origins.Add(__result, new RuntimeMod.FactoryTag { Factory = __originalMethod.DeclaringType.Name + "." + __originalMethod.Name });
        RuntimeMod.Event("debug", "client.created", new { factory = __originalMethod.DeclaringType.Name + "." + __originalMethod.Name, __result.identifier, __result.spriteName, __result.displayName });
    }
    static void BeforeNext() { if (Active) { RuntimeMod.Current.Portrait.Restore(); RuntimeMod.Current.Settings.ResetPortrait(); } }
    static bool SaveAllowed() { if (Active && RuntimeMod.Current.Debugger.BlocksSave) { RuntimeMod.Event("warning", "debug.save.suspended"); return false; } return true; }
    static bool PreviewStatsAllowed(StoreClient __0) => !Active || !RuntimeMod.Current.Debugger.IsSynthetic(__0);
    static bool PreviewSideEffectAllowed() => !Active || !RuntimeMod.Current.Debugger.Preview;
    static void Arrival(MethodBase __originalMethod)
    { if (Active) RuntimeMod.Guard("arrival", () => { RuntimeMod.Current.Portrait.Arrival(); if (__originalMethod.DeclaringType == typeof(StoreUIManager)) RuntimeMod.Current.Debugger.Arrived(); }); }
    static void Left(StoreClientMono __instance)
    {
        if (!Active) return;
        if (RuntimeMod.Current.Portrait.Renderer == __instance.image) { RuntimeMod.Current.Portrait.Restore(); RuntimeMod.Current.Settings.ResetPortrait(); }
        RuntimeMod.Current.Debugger.Left();
    }
    static void Morning()
    {
        if (Active) RuntimeMod.Guard("morning portrait", () =>
        {
            RuntimeMod.Current.Portrait.Restore();
            RuntimeMod.Current.Settings.ResetPortrait();
            RuntimeMod.Current.Portrait.Arrival(StoreUIManager.Instance.clientGameObject.image);
        });
    }
    static void Display(Dialogue dialogue)
    {
        if (Active) RuntimeMod.Guard("dialogue", () =>
        {
            var r = RuntimeMod.Current; r.DialogueTags.TryGetValue(dialogue, out var tag);
            RuntimeMod.Event("debug", "dialogue.display", new { key = tag?.Key, tracked = tag != null, identity = PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint()?.identifier,
                text = dialogue?.plainText?.Substring(0, Math.Min(512, dialogue.plainText.Length)) });
            r.Portrait.Dialogue(dialogue);
        });
    }
    static void Bubble(DialogUIManager __instance) { if (Active) RuntimeMod.Current.Settings.CaptureBubble(__instance); }
    static void Localized(string table, string key, ref string __result)
    {
        if (!Active || __result == null || key == null) return;
        var r = RuntimeMod.Current;
        var locale = LocHelper.GetCurrentLocaleCode();
        __result = r.Catalog.Text(locale, table + "/" + key, r.Catalog.Text(locale, key, __result));
        if (table != "Dialogue" && table != "PhoneDialogue") return;
        // A fresh string retains key provenance even when Unity reuses equal string instances.
        if (__result.Length > 0) __result = new string(__result.ToCharArray());
        var tag = r.TextTags.GetOrCreateValue(__result);
        tag.Keys.Add(key);
    }
    static void BuilderAdd(RichTextBuilder __instance, object[] __args)
    {
        if (!Active) return;
        var r = RuntimeMod.Current;
        RuntimeMod.TextTag tag = null;
        if (__args[0] is string text) r.TextTags.TryGetValue(text, out tag);
        if (__args[0] is RichTextBuilder builder) r.BuilderTags.TryGetValue(builder, out tag);
        if (tag != null) r.BuilderTags.GetOrCreateValue(__instance).Keys.UnionWith(tag.Keys);
    }
    static void BuilderClear(RichTextBuilder __instance) { if (Active) RuntimeMod.Current.BuilderTags.Remove(__instance); }
    static void SetRichText(Dialogue __instance, RichTextBuilder text)
    {
        if (!Active || text == null) return;
        var r = RuntimeMod.Current;
        r.DialogueTags.Remove(__instance);
        if (r.BuilderTags.TryGetValue(text, out var tag) && tag.Keys.Count == 1)
            r.DialogueTags.Add(__instance, new RuntimeMod.DialogueTag { Key = tag.Keys.Single() });
    }
    static void SetText(Dialogue __instance, string text)
    {
        if (!Active || text == null) return;
        var r = RuntimeMod.Current;
        r.DialogueTags.Remove(__instance);
        // Reference identity preserves the originating key, even when text values are equal.
        // Shared references with multiple keys remain ambiguous and are intentionally skipped.
        if (r.TextTags.TryGetValue(text, out var tag) && tag.Keys.Count == 1)
            r.DialogueTags.Add(__instance, new RuntimeMod.DialogueTag { Key = tag.Keys.Single() });
    }
    static void ItemSprite(GameItemElement __instance, ref Sprite __result)
    {
        if (!Active) return;
        var r = RuntimeMod.Current;
        r.Catalog.ItemIcons.TryGetValue(__instance.identifier ?? "", out var entry);
        if (entry?.Value.Icon == null) return;
        try
        {
            var creator = r.Catalog.ItemCreators.TryGetValue(__instance.identifier, out var created) ? created : null;
            Vector2? logicalSize = creator != null ? new Vector2(creator.Value.Grid[0] * (UISettings.current?.gridSize ?? 16), creator.Value.Grid[1] * (UISettings.current?.gridSize ?? 16)) : null;
            __result = r.Assets.Get(entry.Pack, entry.Value.Icon, __result, __instance, logicalSize);
        }
        catch (Exception e) { RuntimeMod.Log("Item fallback " + entry.Id + ": " + e.Message); }
    }
    static void Owned(GameItem item, bool own)
    {
        if (!Active || !own || item == null || PlayerStore.Instance == null) return;
        foreach (var tag in item.state.dict.Keys.Where(x => x.StartsWith("PR_STOCK/", StringComparison.Ordinal)).ToArray())
            RuntimeMod.Current.Host.Set(tag.Substring("PR_STOCK/".Length), "sold");
    }
    static void Special(ref Sprite __result)
    {
        if (!Active || __result == null) return;
        var r = RuntimeMod.Current;
        if (!r.Catalog.Specials.TryGetValue(__result.name, out var entry)) return;
        try { __result = r.Assets.Get(entry.Pack, entry.Value, __result); }
        catch (Exception e) { RuntimeMod.Log("Special fallback " + entry.Id + ": " + e.Message); }
    }
}
